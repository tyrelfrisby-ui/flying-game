// Intercom + radio voice (owner 2026-10-01).
//  MIC   : AVAudioEngine input -> AVAudioSinkNode (realtime, ~5 ms buffers). The intercom is voice-activated (VOX); the
//          radio transmits while the TALK button is held (PTT). The processed voice goes to
//            - the SIDETONE (an AVAudioSourceNode on the same engine, headphones only: on the speaker it would howl),
//            - the clip recorder (VO_ReadClipVoice, at Unity's rate),
//            - the radio transmitter while keyed: 8 kHz G.711 mu-law bytes (VO_ReadTx) that C# sends via the relay.
//  RX    : received mu-law packets (VO_PushRx, one slot per remote pilot) are rendered INTO THE GAME MIX from
//          FlightAudio's audio thread (VO_RenderRx): jitter buffer, 300-3000 Hz radio band, a little drive and static,
//          a squelch "kssh" when the other pilot unkeys. Being in the game mix, clips record it too.
// Lock-free single-producer/single-consumer rings between the threads.
//
// WITH A TV CONNECTED (Ty 2026-11-04, Q7: "radio/intercom audio stays on the phone or headset, not the TV"). iOS gives
// the app one route, so VoiceComms.cs decides (Core TvAudioPolicy) and VO_SetRoute carries it out:
//   keepOffTv  : the play-and-record session WITHOUT AllowAirPlay, so iOS never routes it to an AirPlay receiver;
//   speaker    : overrideOutputAudioPort Speaker, because an HDMI adapter IS an eligible play-and-record output.
// And as a latch that needs no round trip through C#: while the current route has a TV port in it (HDMI, AirPlay,
// DisplayPort), VO_RenderRx adds nothing to the game mix, so a radio call is held silent rather than played on a TV
// (TvAudioPolicy.RadioAudible is the same rule). Off the TV, everything here is exactly as before.
#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>
#include <TargetConditionals.h>
#if !TARGET_OS_IPHONE
#import <CoreAudio/CoreAudio.h>   // Mac app (owner 2026-10-02: Mac/iPad/iPhone parity): output-device query instead of AVAudioSession
#endif
#include <atomic>
#include <cmath>
#include <cstdlib>
#include <cstring>

namespace {

struct FRing {   // float SPSC ring, capacity power of two
    float *buf = nullptr; uint32_t mask = 0;
    std::atomic<uint32_t> w{0}, r{0};
    void init(uint32_t cap) { buf = (float *)calloc(cap, sizeof(float)); mask = cap - 1; }
    uint32_t avail() const { return w.load(std::memory_order_acquire) - r.load(std::memory_order_relaxed); }
    void push(float v) { uint32_t wi = w.load(std::memory_order_relaxed); if (wi - r.load(std::memory_order_acquire) > mask) return; buf[wi & mask] = v; w.store(wi + 1, std::memory_order_release); }
    bool pop(float &v) { uint32_t ri = r.load(std::memory_order_relaxed); if (ri == w.load(std::memory_order_acquire)) return false; v = buf[ri & mask]; r.store(ri + 1, std::memory_order_release); return true; }
    void clear() { r.store(w.load(std::memory_order_acquire), std::memory_order_release); }
};

struct BRing {   // byte SPSC ring
    uint8_t *buf = nullptr; uint32_t mask = 0;
    std::atomic<uint32_t> w{0}, r{0};
    void init(uint32_t cap) { buf = (uint8_t *)calloc(cap, 1); mask = cap - 1; }
    uint32_t avail() const { return w.load(std::memory_order_acquire) - r.load(std::memory_order_relaxed); }
    void push(uint8_t v) { uint32_t wi = w.load(std::memory_order_relaxed); if (wi - r.load(std::memory_order_acquire) > mask) return; buf[wi & mask] = v; w.store(wi + 1, std::memory_order_release); }
    bool pop(uint8_t &v) { uint32_t ri = r.load(std::memory_order_relaxed); if (ri == w.load(std::memory_order_acquire)) return false; v = buf[ri & mask]; r.store(ri + 1, std::memory_order_release); return true; }
    void clear() { r.store(w.load(std::memory_order_acquire), std::memory_order_release); }
};

struct Biquad {   // RBJ cookbook, direct form I
    float b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0, x1 = 0, x2 = 0, y1 = 0, y2 = 0;
    void set(int type, float fs, float f, float q) {   // 0 low-pass, 1 high-pass
        float w = 2.f * (float)M_PI * fminf(f, fs * 0.45f) / fs, c = cosf(w), al = sinf(w) / (2.f * q), a0 = 1.f + al;
        if (type == 0) { b0 = (1 - c) / 2 / a0; b1 = (1 - c) / a0; b2 = b0; }
        else { b0 = (1 + c) / 2 / a0; b1 = -(1 + c) / a0; b2 = b0; }
        a1 = -2 * c / a0; a2 = (1 - al) / a0;
    }
    float run(float x) { float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2; x2 = x1; x1 = x; y2 = y1; y1 = y; return y; }
};

struct Resampler {   // linear interpolation, any ratio; step = input samples per output sample
    float prev = 0; double fr = 0, step = 1;
    template <typename F> void feed(float cur, F emit) { while (fr < 1.0) { emit(prev + (cur - prev) * (float)fr); fr += step; } fr -= 1.0; prev = cur; }
};

// G.711 mu-law
uint8_t MuEncode(float f) {
    int s = (int)lrintf(fmaxf(-1.f, fminf(1.f, f)) * 32635.f);
    int sign = (s >> 8) & 0x80; if (sign) s = -s; s += 0x84;
    int exp = 7; for (int m = 0x4000; (s & m) == 0 && exp > 0; m >>= 1) exp--;
    int mant = (s >> (exp + 3)) & 0x0F;
    return (uint8_t)~(sign | (exp << 4) | mant);
}
float MuDecode(uint8_t u) {
    u = ~u; int sign = u & 0x80, exp = (u >> 4) & 7, mant = u & 0x0F;
    int s = (((mant << 3) + 0x84) << exp) - 0x84;
    return (sign ? -s : s) / 32768.f;
}

uint32_t gNoise = 0x1234567u;
inline float Noise() { gNoise = gNoise * 1664525u + 1013904223u; return (int32_t)gNoise / 2147483648.f; }

// ---- mic side ------------------------------------------------------------------------------
AVAudioEngine *gEngine;
AVAudioSinkNode *gSink;
AVAudioSourceNode *gSide;
std::atomic<bool> gPtt{false}, gHeadphones{false}, gTxEnd{false}, gStarting{false};
std::atomic<int> gState{0};   // 0 off, 1 waiting for permission, 2 running, -1 denied, -2 failed
float gR = 48000, gU = 24000;
Biquad gHp, gLp, gTxLp, gRadHp, gRadLp;
float gEnv = 0, gVoxHold = 0, gGate = 0;
bool gWasPtt = false;
Resampler gToUnity, gTo8k;
FRing gSideRing, gClipOwn;
BRing gTx;
std::atomic<float> gMicLevel{0};
std::atomic<bool> gKeepOffTv{false}, gForceSpeaker{false}, gRxOnTv{false};
/// A TV is connected and NOBODY IS TALKING, so the TV should have the game's sound. See ApplySession: this is the
/// one condition under which the microphone has to be let go of entirely.
std::atomic<bool> gTvWantsAudio{false};
/// VOX IS THE INTERCOM (bit 0 of VO_SetIntercom), and THE INTERCOM IS NOT THE RADIO. Ty 2026-11-10, asked whether
/// VOX should key the transmitter: "No vox so intercom between pilots in the same airplane or solo hearing yourself
/// talk which would allow me to do my narration tutorial videos. In the future we will have vox for multiple
/// airplanes (and their occupants) to keep radio chatter down so several pilots who are flying together can link
/// via vox not talk on the radio."
///
/// So armed, this is a live intercom: the mic is held open, the voice gate opens on voice activity, and the pilot
/// hears himself in his own headset (the sidetone below). Disarmed, the mic opens only while PTT is held. It never
/// touches gTx — the radio stays push-to-talk.
///
/// THE SCOPE WIDENS, AND NOTHING HERE SHOULD ASSUME OTHERWISE. Today the only occupant whose voice this carries is
/// the pilot's own, so "intercom" and "sidetone" are the same signal. Next it is the other seat of this aeroplane,
/// and then the occupants of a FLIGHT of aeroplanes, as a channel beside the radio rather than on it. That is a
/// change of who gSideRing is mixed from and sent to — the receive side already has eight slots and a mixer
/// (VO_PushRx / VO_RenderRx, which is how a remote voice reaches the ears today) — not a change to the gate, the
/// lifecycle or the button. Do not design anything here as two-seat-only.
std::atomic<bool> gVoxArmed{false};
/// The pilot's gated voice may go into CLIPS (bit 1, VoiceComms.NarrateClips). Routing only: it no longer opens or
/// holds the microphone, which is what the old narration tick did and is the whole reason the mic was found open
/// from launch, every launch. VOX decides what listens; this decides only where it goes.
std::atomic<bool> gClipVoice{false};
/// Record permission has been granted, so the engine can be rebuilt without going round the permission prompt again.
std::atomic<bool> gMicGranted{false}, gAskedMic{false}, gReleasing{false};
/// CFAbsoluteTime of the last unkey, for the linger below (0 = never keyed).
std::atomic<double> gUnkeyAt{0};
/// How long the mic is kept after an unkey: long enough for VoiceComms to collect the tail of the transmission and
/// its end marker (VO_ReadTx, the frame after PTT drops), and for a quick re-key to cost no restart.
const double UnkeyLingerSec = 1.5;
NSString *gRouteOutputs = @"";   // comma-separated port types, for C# (guarded by @synchronized)

/// IS THE MICROPHONE WANTED RIGHT NOW?
///
/// Ty 2026-11-10: "The app is listening to my mic even when I do[n't] have PTT pressed unchecked intercom just now".
/// It was, and for the rest of the session: the engine was built the first time the intercom was ticked and then ran
/// for ever, because nothing outside ProcessMic ever looked at gPtt. ApplyMicLifecycle now makes the engine follow
/// this answer, so the mic is HELD only in the cases below and genuinely released in every other.
///
///   PTT held, or just unkeyed  : yes. This is a transmission. The linger covers the tail VoiceComms has still to
///                                collect with VO_ReadTx (the frame AFTER ptt drops) and makes a quick re-key free.
///   VOX armed                  : yes. The pilot pressed the VOX button and asked for a live intercom, which is now
///                                the ONLY way to a hot mic on purpose. Not persisted, so it never outlives the
///                                session, and the button is red the whole time it is true.
///   nothing                    : no. NOTHING PERSISTED CAN REACH THIS. Two settings used to: the intercom tick
///                                (whenever a headset was attached) and clip narration (always). Both are gone from
///                                here — the intercom checkbox no longer exists, VOX is the intercom; and narration
///                                is routing only (gClipVoice). That is the original bug closed at the root rather
///                                than merely made visible: a hot mic can no longer survive a force-quit, which is
///                                what Ty hit — "Force quit and restart did not fix it".
///
/// THE ONE SOURCE OF TRUTH. Whatever this returns, ApplyMicLifecycle makes the engine match it, so "gState is 1 or
/// 2" and "the microphone is open" are the same statement — which is what VO_State reports to the VOX button, so
/// the indicator cannot drift from the behaviour it is indicating.
bool MicWanted() {
    if (gPtt.load()) return true;
    if (gVoxArmed.load()) return true;
    double unkey = gUnkeyAt.load();
    return unkey > 0 && CFAbsoluteTimeGetCurrent() - unkey < UnkeyLingerSec;
}

// ---- the VOX bus: the intercom's own audio path, which is NOT the radio's ----------------------------
//
// Ty 2026-11-10, asked what makes plane-to-plane VOX different from the radio:
//
//   "The difference between plane to plane vox and radio is the ability to mix two or more sounds together
//    without distorting them like radio does"
//
// That sentence is the whole specification, so it lives here. The radio path in ProcessMic is deliberately ugly —
// 350-2800 Hz, tanh at a drive of 3, decimated to 8 kHz, mu-law — and EVERY ONE OF THOSE ARTEFACTS SUMS when two
// of them are stacked, which is the distortion he is describing. That character is right for the radio and stays.
//
// VOX therefore gets its own chain and must never be routed through the radio's to save effort. Nothing on the VOX
// side touches gRadHp/gRadLp, gTxLp, gTo8k, MuEncode or gTx: those all sit inside `if (ptt)` / `if (ptt||gWasPtt)`
// and must stay there. Three rules, which hold for one talker and for N:
//
//   - FLAT. No drive and no band-pass beyond the 180 Hz-5 kHz voice band every path shares. A per-source tanh is
//     precisely the thing that sums into mush, so the VOX voice has none.
//   - PER-SOURCE GAIN IS FIXED, so adding or dropping a talker cannot pump the ones already there.
//   - ONE LIMITER, ON THE BUS, never per source. Speech from several talkers sums incoherently (about sqrt(N)), so
//     the bus is sized for four and the limiter only does anything near full scale.
//
// TODAY THE BUS HAS EXACTLY ONE SOURCE — his own voice, solo, in his own headset — and that is the point rather
// than a simplification: the solo sidetone is the one-source instance of the same mixer. Widening to the other
// seat and then to a flight of aeroplanes is a change of N, not a rewrite, and remote voices already have a way in
// (VO_PushRx / VO_RenderRx, eight slots and a mixer). Whoever adds them sums them onto this bus, flat, and lets
// VoxBusLimit hold the top.
const float VoxVoiceGain = 1.4f;   // flat; matches the small-signal level of the tanh this replaced

/// The VOX bus limiter: linear below the knee so a single quiet talker is untouched, then a soft knee so several
/// talkers compress gracefully together instead of each one clipping on its own.
inline float VoxBusLimit(float v) {
    const float knee = 0.7f;
    float a = fabsf(v);
    if (a <= knee) return v;
    float shaped = knee + (1.f - knee) * tanhf((a - knee) / (1.f - knee));
    return v < 0 ? -shaped : shaped;
}

void ProcessMic(const float *x, uint32_t n) {
    // VOX is the intercom, so VOX is what opens the gate and what plays the sidetone. Ty 2026-11-10: "solo hearing
    // yourself talk which would allow me to do my narration tutorial videos" — hearing himself is the FEATURE here,
    // not a side effect. Narration no longer opens anything; it only says where the voice goes (toClip).
    bool ptt = gPtt.load(), vox = gVoxArmed.load(), hp = gHeadphones.load(), toClip = gClipVoice.load();
    float voxThr = 0.018f, decay = expf(-1.f / (0.05f * gR)), gateK = 1.f - expf(-1.f / (0.004f * gR));
    float peak = 0;
    for (uint32_t i = 0; i < n; i++) {
        float v = gLp.run(gHp.run(x[i]));          // 180 Hz - 5 kHz voice band
        float a = fabsf(v); if (a > peak) peak = a;
        gEnv = a > gEnv ? a : gEnv * decay;
        if (gEnv > voxThr) gVoxHold = 0.7f * gR; else if (gVoxHold > 0) gVoxHold -= 1;
        bool talk = ptt || (vox && gVoxHold > 0);
        gGate += ((talk ? 1.f : 0.f) - gGate) * gateK;   // 4 ms ramps, no clicks
        float voice = v * VoxVoiceGain;                  // VOX: FLAT. See the VOX bus note above before adding to this
        if (ptt || gWasPtt) voice = tanhf(gRadLp.run(gRadHp.run(v)) * 3.f) * 0.55f;   // radio: narrower, driven
        float out = voice * gGate;
        // The sidetone is the intercom's own voice, so VOX owns it. Headphones only: on the loudspeaker it howls.
        //
        // `|| ptt` keeps the sidetone a real radio has while you are keyed, which the old intercom tick used to
        // supply and which otherwise disappeared with it: without this the only way to hear yourself transmit would
        // be to arm VOX, i.e. to hold the mic open between calls, which is the thing being fixed. While PTT is down
        // the mic is open by his finger, so there is nothing to protect here.
        if (hp && (vox || ptt)) gSideRing.push(out);
        // Resampled at Unity's rate either way, so the phase is right whenever the routing comes back on; whether it
        // is KEPT is narration's decision alone.
        gToUnity.feed(out, [toClip](float y) { if (toClip) gClipOwn.push(y); });
        if (ptt) gTo8k.feed(gTxLp.run(v * 2.f), [](float y) { gTx.push(MuEncode(tanhf(y))); });
    }
    if (gWasPtt && !ptt) gTxEnd.store(true);
    gWasPtt = ptt;
    gMicLevel.store(peak);
}

void UpdateRoute() {
    bool hp = false;
#if TARGET_OS_IPHONE
    NSMutableArray<NSString *> *types = [NSMutableArray array];
    bool tv = false;
    for (AVAudioSessionPortDescription *p in AVAudioSession.sharedInstance.currentRoute.outputs) {
        NSString *t = p.portType;
        [types addObject:t];
        if ([t isEqualToString:AVAudioSessionPortHDMI] || [t isEqualToString:AVAudioSessionPortAirPlay] || [t isEqualToString:@"DisplayPort"]) tv = true;
        if ([t isEqualToString:AVAudioSessionPortHeadphones] || [t isEqualToString:AVAudioSessionPortBluetoothA2DP] ||
            [t isEqualToString:AVAudioSessionPortBluetoothLE] || [t isEqualToString:AVAudioSessionPortBluetoothHFP] ||
            [t isEqualToString:AVAudioSessionPortUSBAudio]) hp = true;
    }
    gRxOnTv.store(tv);   // the latch: never mix the radio into a route that is on its way to a TV
    @synchronized ([NSNull null]) { gRouteOutputs = [types componentsJoinedByString:@","]; }
#else
    // Mac: the default output device — anything but the built-in speakers (headphone jack, USB, Bluetooth) counts as
    // headphones, so the sidetone only plays where it can't feed back into the mic.
    AudioObjectID dev = 0; UInt32 sz = sizeof(dev);
    AudioObjectPropertyAddress a = { kAudioHardwarePropertyDefaultOutputDevice, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain };
    if (AudioObjectGetPropertyData(kAudioObjectSystemObject, &a, 0, NULL, &sz, &dev) == noErr && dev != 0) {
        UInt32 transport = 0; sz = sizeof(transport);
        AudioObjectPropertyAddress t = { kAudioDevicePropertyTransportType, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain };
        if (AudioObjectGetPropertyData(dev, &t, 0, NULL, &sz, &transport) == noErr) {
            if (transport != kAudioDeviceTransportTypeBuiltIn) hp = true;
            else {
                UInt32 src = 0; sz = sizeof(src);
                AudioObjectPropertyAddress d = { kAudioDevicePropertyDataSource, kAudioDevicePropertyScopeOutput, kAudioObjectPropertyElementMain };
                if (AudioObjectGetPropertyData(dev, &d, 0, NULL, &sz, &src) == noErr && src == 'hdpn') hp = true;
            }
        }
    }
#endif
    gHeadphones.store(hp);
    if (!hp) gSideRing.clear();
}

/// LET THE MICROPHONE GO: stop the engine and throw the node graph away, so nothing is pulling on the input node.
/// gState goes back to 0 and VO_EnsureMic / ApplyMicLifecycle will build it again when the mic is next wanted.
///
/// This does NOT touch the audio session, because the two callers want different things done about it: ApplySession
/// is mid-decision and goes on to set the category itself, and ApplyMicLifecycle calls ApplySession afterwards —
/// which, with no engine left, puts the session back to plain Playback. That last step is the one that makes the mic
/// CLOSED rather than merely idle: play-and-record is the recording-capable category, and leaving it is what drops
/// the orange recording indicator.
///
/// The transmitter ring is deliberately NOT cleared. VoiceComms reads the tail of a transmission on the frame after
/// PTT drops (VO_ReadTx with its `ended` flag, which is how the far end gets its squelch tail), and MicWanted's
/// linger keeps us out of here until it has.
void ReleaseEngine() {
    if (gEngine == nil) return;
    [gEngine stop];
    if (gSink != nil) { [gEngine detachNode:gSink]; gSink = nil; }
    if (gSide != nil) { [gEngine detachNode:gSide]; gSide = nil; }
    gEngine = nil;
    if (gState.load() == 2) gState = 0;   // -1 denied and -2 failed are latches: they must survive a release
    gSideRing.clear();
    gGate = 0; gEnv = 0; gVoxHold = 0; gWasPtt = false;
    gMicLevel.store(0);
}

#if TARGET_OS_IPHONE
// Use the MICROPHONE IN THE HEADSET when there is one, rather than the one in the phone.
//
// Ty 2026-11-08, flying with Shokz on Bluetooth: "the mic is using the apple mic so I get a lot of mechanical noise
// as I [hold] the phone and my fingers rub on it as I fly". The session asked for AllowBluetoothA2DP, which is the
// option for high-quality PLAYBACK to a Bluetooth device and says nothing about input, so the only microphone iOS
// had to offer was the built-in one — pressed against the hands flying the aeroplane.
//
// AllowBluetooth (HFP) is the one that makes the headset's own microphone an available input, and the preferred
// input is then set explicitly rather than left to iOS, which otherwise keeps the built-in one.
//
// THE TRADE. A Bluetooth headset cannot carry high-quality audio out and a microphone in at the same time: taking
// the microphone moves the whole route from A2DP to HFP, which is mono and narrow-band, so what he HEARS while the
// microphone is live is telephone quality. What is RECORDED is not affected — ClipRecorder takes the game's mix
// inside the app, before it ever reaches the headset — so a tutorial made this way has full-quality game audio with
// a clean voice over it, which is the point. Nothing changes while the microphone is off.
static void PreferHeadsetMic() {
    AVAudioSession *s = AVAudioSession.sharedInstance;
    AVAudioSessionPortDescription *best = nil;
    for (AVAudioSessionPortDescription *p in s.availableInputs) {
        NSString *t = p.portType;
        if ([t isEqualToString:AVAudioSessionPortBluetoothHFP]) { best = p; break; }          // the headset: first choice
        if (!best && ([t isEqualToString:AVAudioSessionPortHeadsetMic] ||
                      [t isEqualToString:AVAudioSessionPortUSBAudio])) best = p;              // wired, still off the hands
    }
    if (best) [s setPreferredInput:best error:nil];
}

extern "C" void FlyingGame_SetPlaybackAudioSession(void);   // AudioSessionPlayback.mm

/// The session the voice needs right now (main thread). With the mic running, or with voice in use and a TV connected,
/// it is play-and-record; AirPlay is allowed only when the TV is not to be kept off. Otherwise the plain playback
/// session the game starts with (AudioSessionPlayback.mm), so a TV gets the game's sound when no one is talking.
void ApplySession() {
    AVAudioSession *s = AVAudioSession.sharedInstance;
    bool keepOff = gKeepOffTv.load(), engine = gEngine != nil;
    NSError *err = nil;
    // PLAY-AND-RECORD CANNOT OUTPUT TO A TV. iOS offers that category the speaker, the receiver, headphones and
    // Bluetooth — not HDMI. So while the voice engine exists the TV is silent whatever options are set, which is
    // what Ty kept reporting with the game already on the screen: "The phone has audio but not the tv". Removing
    // DefaultToSpeaker was not enough and could not have been.
    //
    // So when a TV is connected and no one is talking, the microphone is let go: the engine stops, the session
    // goes back to plain Playback, and the TV gets the sound. VO_EnsureMic starts it again the moment the mic is
    // actually wanted, which is what the PTT already calls. Nothing is lost but the idle mic.
    // ...and MicWanted has the final say: with clip narration ticked the mic has to stay (its buffer is always
    // rolling), and releasing it here would only have ApplyMicLifecycle build it straight back — a loop. VoiceComms
    // counts narration as voice in use for exactly that reason, so this should not come up; the guard is cheap.
    if (gTvWantsAudio.load() && !keepOff && gEngine != nil && !MicWanted()) {
        ReleaseEngine();     // VO_EnsureMic / ApplyMicLifecycle will build it again when the mic is next wanted
        engine = false;
        NSLog(@"[VoiceIO] TV connected and nobody talking: released the mic so the TV can have the audio");
    }
    if (engine || keepOff) {
        // DEFAULTTOSPEAKER, ALWAYS, IN THIS CATEGORY — and read the two paragraphs above before touching it, because
        // this line has now been wrong twice in both directions.
        //
        // It was first set unconditionally, and Ty 2026-11-10: "Still no sound on the tv that has not been fixed".
        // The diagnosis was that DefaultToSpeaker pins the route to the phone's speaker, so an idle PTT kept HDMI
        // silent for ever. The pinning is real, but it was not the cause and removing the option could not have been
        // the cure: play-and-record HAS NO HDMI OUTPUT AT ALL. The cure was the release above — no engine, no
        // play-and-record, and the TV gets the game. That left this line making the option conditional for no
        // reason, and it cost the opposite bug the same evening: Ty, intercom ticked and no TV anywhere near him,
        // "I just realized there is no audio". Without DefaultToSpeaker, play-and-record's default output is the
        // RECEIVER — the earpiece — so the game was playing into the top of the phone, inaudible in the hand.
        //
        // So: whenever this category is set at all, there is no TV to route to, and the only two candidates are the
        // loudspeaker and the earpiece. The earpiece is never the answer. DefaultToSpeaker is only a DEFAULT — a
        // headset, CarPlay or a Bluetooth headset still takes the route when one is attached, which is what keeps
        // the headset intercom working — so there is nothing left for it to break.
        //
        // AllowAirPlay goes with it: it was there to let iOS pick the TV, which this category cannot do anyway, and
        // an AirPlay route it did pick would be a TV the voice is supposed to stay off.
        AVAudioSessionCategoryOptions o = AVAudioSessionCategoryOptionAllowBluetooth |
                                          AVAudioSessionCategoryOptionAllowBluetoothA2DP |
                                          AVAudioSessionCategoryOptionMixWithOthers |
                                          AVAudioSessionCategoryOptionDefaultToSpeaker;
        if (![s.category isEqualToString:AVAudioSessionCategoryPlayAndRecord] || s.categoryOptions != o)
            [s setCategory:AVAudioSessionCategoryPlayAndRecord withOptions:o error:&err];
        [s overrideOutputAudioPort:gForceSpeaker.load() ? AVAudioSessionPortOverrideSpeaker : AVAudioSessionPortOverrideNone error:nil];
        if (engine) PreferHeadsetMic();   // the headset's own microphone, not the one under his fingers
    } else if (![s.category isEqualToString:AVAudioSessionCategoryPlayback]) {
        FlyingGame_SetPlaybackAudioSession();
    }
    [s setActive:YES error:nil];
    if (err) NSLog(@"[VoiceIO] session: %@", err);
    UpdateRoute();
    NSLog(@"[VoiceIO] route: keepOffTv %d speaker %d -> %@ (%@)", (int)keepOff, (int)gForceSpeaker.load(), s.category, gRouteOutputs);
}
#endif

void StartEngine() {
    NSError *err = nil;
#if TARGET_OS_IPHONE
    AVAudioSession *s = AVAudioSession.sharedInstance;
    gEngine = [AVAudioEngine new];   // (set first: ApplySession keys play-and-record off it)
    ApplySession();
    [s setPreferredIOBufferDuration:0.005 error:nil];
    [s setActive:YES error:nil];
    PreferHeadsetMic();
#endif
    if (gEngine == nil) gEngine = [AVAudioEngine new];
    AVAudioInputNode *in = gEngine.inputNode;
    AVAudioFormat *inF = [in inputFormatForBus:0];
    if (inF.sampleRate <= 0 || inF.channelCount == 0) { gState = -2; gEngine = nil; NSLog(@"[VoiceIO] no input format"); return; }
    gR = (float)inF.sampleRate;
    gHp.set(1, gR, 180.f, 0.707f); gLp.set(0, gR, 5000.f, 0.707f);
    gRadHp.set(1, gR, 350.f, 0.9f); gRadLp.set(0, gR, 2800.f, 0.9f);
    gTxLp.set(0, gR, 3400.f, 0.707f);
    gToUnity.step = gR / gU; gTo8k.step = gR / 8000.f;
    gSink = [[AVAudioSinkNode alloc] initWithReceiverBlock:^OSStatus(const AudioTimeStamp *ts, AVAudioFrameCount frames, const AudioBufferList *abl) {
        if (abl->mNumberBuffers > 0 && abl->mBuffers[0].mData) ProcessMic((const float *)abl->mBuffers[0].mData, frames);
        return noErr;
    }];
    [gEngine attachNode:gSink];
    [gEngine connect:in to:gSink format:inF];
    AVAudioFormat *mono = [[AVAudioFormat alloc] initStandardFormatWithSampleRate:gR channels:1];
    gSide = [[AVAudioSourceNode alloc] initWithFormat:mono renderBlock:^OSStatus(BOOL *silence, const AudioTimeStamp *ts, AVAudioFrameCount frames, AudioBufferList *abl) {
        float *y = (float *)abl->mBuffers[0].mData;
        bool any = false;
        // THE VOX BUS. One source today (his own voice); the limiter is here, on the sum, rather than on any one
        // voice, so that adding the other seat and then other aeroplanes is a change of N. See the VOX bus note.
        for (AVAudioFrameCount i = 0; i < frames; i++) { float v = 0; if (gSideRing.pop(v)) any = true; y[i] = VoxBusLimit(v); }
        // Keep the sidetone latency at a few buffers: drop the backlog if it builds up.
        if (gSideRing.avail() > (uint32_t)(0.03f * gR)) gSideRing.clear();
        *silence = !any;
        return noErr;
    }];
    [gEngine attachNode:gSide];
    [gEngine connect:gSide to:gEngine.mainMixerNode format:mono];
    [gEngine prepare];
    if (![gEngine startAndReturnError:&err]) { gState = -2; gEngine = nil; NSLog(@"[VoiceIO] engine start failed: %@", err); return; }
    if (![gEngine startAndReturnError:&err]) { gState = -2; NSLog(@"[VoiceIO] engine start failed: %@", err); return; }
#if TARGET_OS_IPHONE
    // A headset connected or dropped mid-flight: take its microphone, or hand the input back to the phone.
    [[NSNotificationCenter defaultCenter] addObserverForName:AVAudioSessionRouteChangeNotification object:nil queue:nil
                                                  usingBlock:^(NSNotification *n) { PreferHeadsetMic(); UpdateRoute(); }];
#endif
    // A device change (Mac: headphones plugged in / output switched) reconfigures the engine: restart it and re-check.
    [[NSNotificationCenter defaultCenter] addObserverForName:AVAudioEngineConfigurationChangeNotification object:gEngine queue:NSOperationQueue.mainQueue
                                                  usingBlock:^(NSNotification *n) { NSError *e = nil; if (!gEngine.isRunning) [gEngine startAndReturnError:&e]; UpdateRoute(); }];
    UpdateRoute();
    gState = 2;
    NSLog(@"[VoiceIO] mic running at %.0f Hz, headphones %d", gR, (int)gHeadphones.load());
}

/// MAKE THE MICROPHONE'S LIFETIME FOLLOW MicWanted. Called from VO_SetPtt and VO_SetIntercom, so it runs every frame
/// and must do nothing at all unless the answer has changed — which is what the gState / gEngine tests below are.
///
/// Both directions go through the main queue, because AVAudioEngine and AVAudioSession want to be driven from there
/// and because the release must not happen inside the same Unity frame that is still reading the transmission out of
/// gTx. The dispatched block asks MicWanted again, so a PTT press and release in the same frame cancels itself out.
///
/// WHY A TEARDOWN AND NOT JUST A PAUSE. AVAudioEngine has no "stop the input and leave the rest running" — the
/// nearest thing is [engine pause], which keeps the session in play-and-record, and holding play-and-record is
/// itself one of tonight's two bugs (it has no HDMI output, so it silences a TV: see ApplySession). Leaving the
/// category is also what actually closes the mic, rather than leaving it open with the samples thrown away: a tap
/// that runs and discards still shows the orange recording indicator and still counts as listening. So the choice
/// is forced by the TV, not taken freely.
///
/// WHAT THE TEARDOWN COSTS. On a key-down with permission already granted: one hop to the main queue (0-16 ms),
/// setCategory to play-and-record (tens of ms), setActive (tens of ms, it is a route change), then building five
/// nodes, prepare and start (tens of ms) — call it 70-200 ms before the first sample, worst case ~300 ms on an
/// awkward route change. That is the first syllable of a transmission, and it is the price of the mic being shut
/// between them. Three things keep it off the normal path: the permission round trip is skipped after the first
/// grant (gMicGranted), the 1.5 s linger after an unkey means a conversation pays it once rather than per call, and
/// a headset intercom never pays it at all because MicWanted is already true there. If Ty reports clipped first
/// words, the next step is to pre-start the engine when the TALK button appears (RadioAvailable) rather than to go
/// back to holding the mic all session.
void ApplyMicLifecycle() {
    bool want = MicWanted();
    if (want) {
        if (gState.load() != 0 || !gMicGranted.load()) return;   // running, failed, denied, or not asked yet
        gState = 1;
        dispatch_async(dispatch_get_main_queue(), ^{
            if (MicWanted()) StartEngine();
            else if (gState.load() == 1) gState = 0;
        });
        return;
    }
    if (gEngine == nil || gReleasing.exchange(true)) return;
    dispatch_async(dispatch_get_main_queue(), ^{
        if (!MicWanted() && gEngine != nil) {
            ReleaseEngine();
#if TARGET_OS_IPHONE
            ApplySession();   // no engine left, so this puts the session back to plain Playback
#endif
            NSLog(@"[VoiceIO] nobody is talking: let the microphone go");
        }
        gReleasing = false;
    });
}

// ---- radio receive ---------------------------------------------------------------------------
const int Slots = 8;
struct RxSlot {
    BRing bytes;
    std::atomic<bool> endFlag{false};
    bool playing = false;
    float prev = 0, cur = 0; double fr = 0;
    float starve = 0, tail = 0;
    Biquad hp, lp;
};
RxSlot gRx[Slots];
float gRxRate = 0;
std::atomic<float> gRxVolume{1.f};
std::atomic<int> gRxActiveMask{0};

void InitRx(float rate) {
    gRxRate = rate;
    for (int i = 0; i < Slots; i++) { gRx[i].hp.set(1, rate, 300.f, 0.9f); gRx[i].lp.set(0, rate, 3000.f, 0.9f); }
}

} // namespace

extern "C" {

/// Unity's output rate (for the clip voice and the receiver); call once before anything else.
void VO_Init(int unityRate)
{
    static bool done = false;
    if (done) return;
    done = true;
    gU = (float)unityRate;
    gSideRing.init(1 << 14); gClipOwn.init(1 << 16); gTx.init(1 << 15);
    for (int i = 0; i < Slots; i++) gRx[i].bytes.init(1 << 15);
    InitRx(gU);
#if TARGET_OS_IPHONE
    // From the start, not only once the mic runs: the radio can be received without ever talking, and the TV latch
    // (gRxOnTv) has to know where the route is for that too.
    [[NSNotificationCenter defaultCenter] addObserverForName:AVAudioSessionRouteChangeNotification object:nil queue:nil
                                                  usingBlock:^(NSNotification *n) { UpdateRoute(); }];
    UpdateRoute();
#endif
}

/// Q7: carry out Core TvAudioPolicy's decision (VoiceComms.cs calls this when it changes). Main thread.
/// A TV is connected and voice is idle (VoiceComms, from TvAudioPolicy's own inputs).
extern "C" void VO_SetTvWantsAudio(int wants)
{
    bool w = wants != 0;
    if (gTvWantsAudio.load() == w) return;
    gTvWantsAudio = w;
#if TARGET_OS_IPHONE
    // ApplySession is iOS-only (the Mac has no AVAudioSession to juggle), and this call was not guarded — so the Mac
    // bundle, which tools/native-mac/build.sh builds from this very file, had stopped compiling. Nothing on the Mac
    // has a TV route to keep off, so there is nothing to do there.
    dispatch_async(dispatch_get_main_queue(), ^{ ApplySession(); });
#endif
}

void VO_SetRoute(int keepOffTv, int forceSpeaker)
{
    gKeepOffTv = keepOffTv != 0;
    gForceSpeaker = forceSpeaker != 0;
#if TARGET_OS_IPHONE
    ApplySession();
#endif
}

/// The current outputs as comma-separated AVAudioSessionPort types ("HDMI", "Speaker", ...). Empty on the Mac.
int VO_RouteOutputs(char *dst, int max)
{
    if (max <= 0) return 0;
    NSString *r;
    @synchronized ([NSNull null]) { r = gRouteOutputs; }
    const char *u = r.UTF8String ?: "";
    int n = (int)strnlen(u, (size_t)max - 1);
    memcpy(dst, u, (size_t)n);
    dst[n] = 0;
    return n;
}

/// Ask for the microphone (first time only) and start the mic engine. Returns the state (see gState).
/// VoiceComms calls this every frame once the mic has ever been wanted, so it must be idempotent AND must not
/// re-open a microphone that ApplyMicLifecycle has just let go of. It therefore does two separate things: ask for
/// permission, once ever; and otherwise hand the decision to ApplyMicLifecycle, which starts the engine only while
/// MicWanted says so. Asking early is deliberate — the alternative is the permission sheet appearing on a key-down
/// and eating the start of the transmission.
int VO_EnsureMic(void)
{
    if (gMicGranted.load()) { ApplyMicLifecycle(); return gState.load(); }
    int st = gState.load();
    if (st != 0 || gAskedMic.exchange(true)) return st;
    gState = 1;
    void (^answered)(BOOL) = ^(BOOL granted) {
        dispatch_async(dispatch_get_main_queue(), ^{
            gMicGranted = granted;
            if (!granted) { gState = -1; return; }
            gState = 0;
            ApplyMicLifecycle();   // only if it is still wanted: the intercom alone, with nothing to listen on, is not
        });
    };
#if TARGET_OS_IPHONE
    [AVAudioSession.sharedInstance requestRecordPermission:answered];
#else
    [AVCaptureDevice requestAccessForMediaType:AVMediaTypeAudio completionHandler:answered];
#endif
    return 1;
}

/// 0 off, 1 asking or starting, 2 running, -1 denied, -2 failed. Because ApplyMicLifecycle keeps the engine's
/// lifetime equal to MicWanted(), 1 or 2 here means THE MICROPHONE IS OPEN — which is what the VOX button's live
/// indicator reads (VoiceComms.MicOpen). 1 is counted as open on purpose: it means the mic is about to be, and the
/// safe direction for an indicator like this is to light up early rather than late.
int VO_State(void) { return gState.load(); }

/// What the pilot has asked of the microphone, as a bit mask. It rides this one call rather than new exports,
/// because the committed Mac bundle is a tracked binary and a symbol it does not have would throw on the first call;
/// an old bundle reading `on != 0` sees either bit as simply "on", which is what this call used to mean.
///
/// Two bits, because there are now two controls and they mean two different things:
///
///   bit 0  THE VOX BUTTON is armed — the intercom is live (Ty 2026-11-10: "intercom between pilots in the same
///          airplane or solo hearing yourself talk"). Holds the microphone, opens the voice gate on voice activity,
///          and plays the sidetone. The only hot mic a pilot can have, asked for on purpose, and not persisted.
///   bit 1  MY VOICE MAY GO INTO CLIPS (VoiceComms.NarrateClips). Routing only — it opens nothing and holds
///          nothing. VOX decides what listens; this decides only where it goes.
///
/// The Options "intercom" checkbox that used to be bit 0 is GONE, not moved: it and the VOX button were the same
/// idea, and keeping both is how a persisted tick ended up doing invisibly what a button should have shown.
void VO_SetIntercom(int on)
{
    gVoxArmed = (on & 1) != 0;
    bool toClip = (on & 2) != 0;
    // Dropping the routing drops what is already queued, so re-ticking it cannot splice minutes-old speech into a
    // clip (VO_ReadClipVoice reads whatever is in there).
    if (gClipVoice.exchange(toClip) && !toClip) gClipOwn.clear();
    ApplyMicLifecycle();
}

void VO_SetPtt(int on)
{
    bool ptt = on != 0;
    if (gPtt.exchange(ptt) && !ptt) gUnkeyAt = CFAbsoluteTimeGetCurrent();   // keyed -> unkeyed: start the linger
    ApplyMicLifecycle();
}
int VO_Headphones(void) { return gHeadphones.load() ? 1 : 0; }
float VO_MicLevel(void) { return gMicLevel.load(); }

/// Transmitter: up to max mu-law bytes; *ended = 1 once the mic has been unkeyed and everything is read.
int VO_ReadTx(unsigned char *dst, int max, int *ended)
{
    int n = 0; uint8_t b;
    while (n < max && gTx.pop(b)) dst[n++] = b;
    *ended = 0;
    if (gTx.avail() == 0 && gTxEnd.exchange(false)) *ended = 1;
    return n;
}
int VO_TxAvailable(void) { return (int)gTx.avail(); }

/// Received packet from a remote pilot (slot 0..7): mu-law bytes, end = that pilot unkeyed.
void VO_PushRx(int slot, const unsigned char *data, int len, int end)
{
    if (slot < 0 || slot >= Slots) return;
    for (int i = 0; i < len; i++) gRx[slot].bytes.push(data[i]);
    if (end) gRx[slot].endFlag = true;
}

void VO_SetRxVolume(float v) { gRxVolume = v; }
int VO_RxActiveMask(void) { return gRxActiveMask.load(); }

/// Unity audio thread (FlightAudio): ADD the radio receivers into an interleaved game buffer.
void VO_RenderRx(float *data, int frames, int channels)
{
    if (gRxRate <= 0) return;
    double step = 8000.0 / gRxRate;
    float vol = gRxOnTv.load() ? 0.f : gRxVolume.load(), tailLen = 0.14f * gRxRate;   // Q7: never into a TV
    int mask = 0;
    for (int s = 0; s < Slots; s++) {
        RxSlot &x = gRx[s];
        if (!x.playing) {
            // Jitter buffer: start once 150 ms is queued (or a whole short transmission has arrived).
            if (x.bytes.avail() >= 1200 || (x.endFlag.load() && x.bytes.avail() > 0)) { x.playing = true; x.starve = 0; x.tail = 0; }
            else if (x.tail <= 0) continue;
        }
        mask |= 1 << s;
        for (int i = 0; i < frames; i++) {
            float v = 0;
            if (x.playing) {
                x.fr += step;
                while (x.fr >= 1.0) {
                    x.fr -= 1.0; x.prev = x.cur;
                    uint8_t b;
                    if (x.bytes.pop(b)) { x.cur = MuDecode(b); x.starve = 0; }
                    else {
                        x.cur = 0;
                        x.starve += 1;
                        bool ended = x.endFlag.load();
                        if (ended || x.starve > 3200) {   // unkeyed, or 400 ms with nothing: carrier drops -> squelch tail
                            x.playing = false; x.endFlag = false; x.tail = tailLen; x.fr = 0;
                            break;
                        }
                    }
                }
                v = x.prev + (x.cur - x.prev) * (float)x.fr + Noise() * 0.012f;   // voice + a little static
            }
            if (x.tail > 0) { v += Noise() * 0.35f * (x.tail / tailLen); x.tail -= 1; }   // "kssh"
            v = tanhf(x.lp.run(x.hp.run(v)) * 2.2f) * 0.5f * vol;
            for (int c = 0; c < channels; c++) data[i * channels + c] += v;
        }
    }
    gRxActiveMask = mask;
}

/// Unity audio thread (ClipRecorder): ADD this pilot's own processed voice into the clip's audio (not the game's).
void VO_ReadClipVoice(float *data, int frames, int channels)
{
    for (int i = 0; i < frames; i++) {
        float v = 0;
        if (!gClipOwn.pop(v)) break;
        for (int c = 0; c < channels; c++) data[i * channels + c] += v;
    }
    if (gClipOwn.avail() > (uint32_t)(0.25f * gU)) gClipOwn.clear();   // never let it lag the picture
}

}

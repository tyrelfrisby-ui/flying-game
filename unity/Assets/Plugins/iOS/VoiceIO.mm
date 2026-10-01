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
#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>
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
std::atomic<bool> gIntercom{false}, gPtt{false}, gHeadphones{false}, gTxEnd{false}, gStarting{false};
std::atomic<int> gState{0};   // 0 off, 1 waiting for permission, 2 running, -1 denied, -2 failed
float gR = 48000, gU = 24000;
Biquad gHp, gLp, gTxLp, gRadHp, gRadLp;
float gEnv = 0, gVoxHold = 0, gGate = 0;
bool gWasPtt = false;
Resampler gToUnity, gTo8k;
FRing gSideRing, gClipOwn;
BRing gTx;
std::atomic<float> gMicLevel{0};

void ProcessMic(const float *x, uint32_t n) {
    bool ptt = gPtt.load(), ic = gIntercom.load(), hp = gHeadphones.load();
    float voxThr = 0.018f, decay = expf(-1.f / (0.05f * gR)), gateK = 1.f - expf(-1.f / (0.004f * gR));
    float peak = 0;
    for (uint32_t i = 0; i < n; i++) {
        float v = gLp.run(gHp.run(x[i]));          // 180 Hz - 5 kHz voice band
        float a = fabsf(v); if (a > peak) peak = a;
        gEnv = a > gEnv ? a : gEnv * decay;
        if (gEnv > voxThr) gVoxHold = 0.7f * gR; else if (gVoxHold > 0) gVoxHold -= 1;
        bool talk = ptt || (ic && gVoxHold > 0);
        gGate += ((talk ? 1.f : 0.f) - gGate) * gateK;   // 4 ms ramps, no clicks
        float voice = tanhf(v * 2.5f) * 0.6f;            // a little intercom compression
        if (ptt || gWasPtt) voice = tanhf(gRadLp.run(gRadHp.run(v)) * 3.f) * 0.55f;   // radio: narrower, driven
        float out = voice * gGate;
        if (hp) gSideRing.push(out);
        gToUnity.feed(out, [](float y) { gClipOwn.push(y); });
        if (ptt) gTo8k.feed(gTxLp.run(v * 2.f), [](float y) { gTx.push(MuEncode(tanhf(y))); });
    }
    if (gWasPtt && !ptt) gTxEnd.store(true);
    gWasPtt = ptt;
    gMicLevel.store(peak);
}

void UpdateRoute() {
    bool hp = false;
    for (AVAudioSessionPortDescription *p in AVAudioSession.sharedInstance.currentRoute.outputs) {
        NSString *t = p.portType;
        if ([t isEqualToString:AVAudioSessionPortHeadphones] || [t isEqualToString:AVAudioSessionPortBluetoothA2DP] ||
            [t isEqualToString:AVAudioSessionPortBluetoothLE] || [t isEqualToString:AVAudioSessionPortBluetoothHFP] ||
            [t isEqualToString:AVAudioSessionPortUSBAudio]) hp = true;
    }
    gHeadphones.store(hp);
    if (!hp) gSideRing.clear();
}

void StartEngine() {
    NSError *err = nil;
    AVAudioSession *s = AVAudioSession.sharedInstance;
    [s setCategory:AVAudioSessionCategoryPlayAndRecord
       withOptions:AVAudioSessionCategoryOptionDefaultToSpeaker | AVAudioSessionCategoryOptionAllowBluetoothA2DP | AVAudioSessionCategoryOptionMixWithOthers
             error:&err];
    [s setPreferredIOBufferDuration:0.005 error:nil];
    [s setActive:YES error:nil];
    gEngine = [AVAudioEngine new];
    AVAudioInputNode *in = gEngine.inputNode;
    AVAudioFormat *inF = [in inputFormatForBus:0];
    if (inF.sampleRate <= 0 || inF.channelCount == 0) { gState = -2; NSLog(@"[VoiceIO] no input format"); return; }
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
        for (AVAudioFrameCount i = 0; i < frames; i++) { float v = 0; if (gSideRing.pop(v)) any = true; y[i] = v; }
        // Keep the sidetone latency at a few buffers: drop the backlog if it builds up.
        if (gSideRing.avail() > (uint32_t)(0.03f * gR)) gSideRing.clear();
        *silence = !any;
        return noErr;
    }];
    [gEngine attachNode:gSide];
    [gEngine connect:gSide to:gEngine.mainMixerNode format:mono];
    [gEngine prepare];
    if (![gEngine startAndReturnError:&err]) { gState = -2; NSLog(@"[VoiceIO] engine start failed: %@", err); return; }
    [[NSNotificationCenter defaultCenter] addObserverForName:AVAudioSessionRouteChangeNotification object:nil queue:nil
                                                  usingBlock:^(NSNotification *n) { UpdateRoute(); }];
    [[NSNotificationCenter defaultCenter] addObserverForName:AVAudioEngineConfigurationChangeNotification object:gEngine queue:NSOperationQueue.mainQueue
                                                  usingBlock:^(NSNotification *n) { NSError *e = nil; if (!gEngine.isRunning) [gEngine startAndReturnError:&e]; }];
    UpdateRoute();
    gState = 2;
    NSLog(@"[VoiceIO] mic running at %.0f Hz, headphones %d", gR, (int)gHeadphones.load());
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
}

/// Ask for the microphone (first time only) and start the mic engine. Returns the state (see gState).
int VO_EnsureMic(void)
{
    int st = gState.load();
    if (st != 0) return st;
    gState = 1;
    [AVAudioSession.sharedInstance requestRecordPermission:^(BOOL granted) {
        dispatch_async(dispatch_get_main_queue(), ^{ if (granted) StartEngine(); else gState = -1; });
    }];
    return 1;
}

int VO_State(void) { return gState.load(); }
void VO_SetIntercom(int on) { gIntercom = on != 0; }
void VO_SetPtt(int on) { gPtt = on != 0; }
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
    float vol = gRxVolume.load(), tailLen = 0.14f * gRxRate;
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

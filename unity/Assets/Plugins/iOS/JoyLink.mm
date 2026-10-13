// JOYSTICK LINK (owner 2026-10-12: "option 1" — the RC radio's dongle stays in the Mac and the iPad/iPhone flies from it).
// The Mac game advertises a Bonjour UDP service on the local network and sends the joystick's raw channels to every
// device that says hello; the iPad/iPhone finds the first Mac, says hello twice a second, and keeps the latest packet.
// Only the transport lives here (Network.framework, the same source in the iOS build and the Mac AeroNative bundle);
// the packet is built and read in C# (JoyLink.cs). Nothing leaves the local network.
#import <Foundation/Foundation.h>
#import <Network/Network.h>
#include <string.h>
#include <vector>

static const char *kService = "_aerojoy._udp";
static dispatch_queue_t Q() { static dispatch_queue_t q; static dispatch_once_t o; dispatch_once(&o, ^{ q = dispatch_queue_create("aero.joylink", DISPATCH_QUEUE_SERIAL); }); return q; }

// ---- sender (Mac) ----
struct Peer { nw_connection_t c; double heardAt; };
static nw_listener_t gListener;
static std::vector<Peer> gPeers;   // (touched only on Q)
static int gListenerState;         // 0 off, 1 starting, 2 advertising, 3 failed

static double Now() { return [NSDate timeIntervalSinceReferenceDate]; }

static void ReceiveLoop(nw_connection_t c)
{
    nw_connection_receive_message(c, ^(dispatch_data_t content, nw_content_context_t ctx, bool complete, nw_error_t err) {
        if (err != nullptr) return;
        for (auto &p : gPeers) if (p.c == c) p.heardAt = Now();
        ReceiveLoop(c);
    });
}

extern "C" int JL_ServeStart(const char *name)
{
    if (gListener != nullptr) return gListenerState;
    nw_parameters_t params = nw_parameters_create_secure_udp(NW_PARAMETERS_DISABLE_PROTOCOL, NW_PARAMETERS_DEFAULT_CONFIGURATION);
    gListener = nw_listener_create(params);
    if (gListener == nullptr) { gListenerState = 3; return 3; }
    NSString *n = name ? [NSString stringWithUTF8String:name] : @"Aero Playground";
    nw_advertise_descriptor_t ad = nw_advertise_descriptor_create_bonjour_service(n.UTF8String, kService, nullptr);
    nw_listener_set_advertise_descriptor(gListener, ad);
    nw_listener_set_queue(gListener, Q());
    nw_listener_set_state_changed_handler(gListener, ^(nw_listener_state_t st, nw_error_t err) {
        gListenerState = st == nw_listener_state_ready ? 2 : st == nw_listener_state_failed ? 3 : st == nw_listener_state_cancelled ? 0 : 1;
    });
    nw_listener_set_new_connection_handler(gListener, ^(nw_connection_t c) {
        nw_connection_set_queue(c, Q());
        nw_connection_set_state_changed_handler(c, ^(nw_connection_state_t st, nw_error_t err) {
            if (st == nw_connection_state_failed || st == nw_connection_state_cancelled)
                for (size_t i = 0; i < gPeers.size(); i++) if (gPeers[i].c == c) { gPeers.erase(gPeers.begin() + i); break; }
        });
        gPeers.push_back({ c, Now() });
        nw_connection_start(c);
        ReceiveLoop(c);
    });
    gListenerState = 1;
    nw_listener_start(gListener);
    return gListenerState;
}

extern "C" void JL_ServeStop()
{
    if (gListener == nullptr) return;
    nw_listener_t l = gListener; gListener = nullptr; gListenerState = 0;
    dispatch_async(Q(), ^{ for (auto &p : gPeers) nw_connection_cancel(p.c); gPeers.clear(); nw_listener_cancel(l); });
}

/// 0 off · 1 starting · 2 advertising · 3 failed; *peers = devices heard from in the last 3 s.
extern "C" int JL_ServeState(int *peers)
{
    __block int n = 0;
    dispatch_sync(Q(), ^{ double t = Now(); for (auto &p : gPeers) if (t - p.heardAt < 3.0) n++; });
    if (peers) *peers = n;
    return gListenerState;
}

extern "C" void JL_Send(const unsigned char *buf, int len)
{
    if (gListener == nullptr || len <= 0) return;
    dispatch_data_t d = dispatch_data_create(buf, (size_t)len, nullptr, DISPATCH_DATA_DESTRUCTOR_DEFAULT);
    dispatch_async(Q(), ^{
        double t = Now();
        for (auto &p : gPeers)
            if (t - p.heardAt < 3.0) nw_connection_send(p.c, d, NW_CONNECTION_DEFAULT_MESSAGE_CONTEXT, true, ^(nw_error_t e) {});
    });
}

// ---- receiver (iPad / iPhone) ----
static nw_browser_t gBrowser;
static nw_connection_t gConn;
static dispatch_source_t gHello;
static int gRecvState;                       // 0 off, 1 searching, 2 connected
static unsigned char gLatest[1500], gLatestProf[1500];
static int gLatestLen, gLatestProfLen;
static double gLatestAt;
static char gPeerName[128];

static void Connect(nw_endpoint_t ep);
static void Drop()
{
    if (gConn != nullptr) { nw_connection_cancel(gConn); gConn = nullptr; }
    if (gRecvState == 2) gRecvState = 1;
}

static void RecvLoop(nw_connection_t c)
{
    nw_connection_receive_message(c, ^(dispatch_data_t content, nw_content_context_t ctx, bool complete, nw_error_t err) {
        if (c != gConn) return;
        if (err != nullptr) { Drop(); return; }
        if (content != nullptr)
        {
            dispatch_data_apply(content, ^bool(dispatch_data_t region, size_t offset, const void *buf, size_t size) {
                if (offset == 0 && size >= 4 && size <= sizeof(gLatest))
                {
                    const unsigned char *b = (const unsigned char *)buf;
                    if (b[3] == 'P') { memcpy(gLatestProf, buf, size); gLatestProfLen = (int)size; }   // "AJ1P": the Mac's calibration
                    else { memcpy(gLatest, buf, size); gLatestLen = (int)size; gLatestAt = Now(); }
                }
                return true;
            });
            gRecvState = 2;
        }
        RecvLoop(c);
    });
}

static void Connect(nw_endpoint_t ep)
{
    if (gConn != nullptr) return;
    nw_parameters_t params = nw_parameters_create_secure_udp(NW_PARAMETERS_DISABLE_PROTOCOL, NW_PARAMETERS_DEFAULT_CONFIGURATION);
    nw_connection_t c = nw_connection_create(ep, params);
    gConn = c;
    const char *n = nw_endpoint_get_bonjour_service_name(ep);
    strlcpy(gPeerName, n ? n : "", sizeof(gPeerName));
    nw_connection_set_queue(c, Q());
    nw_connection_set_state_changed_handler(c, ^(nw_connection_state_t st, nw_error_t err) {
        if ((st == nw_connection_state_failed || st == nw_connection_state_cancelled) && c == gConn) { gConn = nullptr; if (gRecvState == 2) gRecvState = 1; }
    });
    nw_connection_start(c);
    RecvLoop(c);
}

extern "C" void JL_RecvStart()
{
    if (gBrowser != nullptr) return;
    gRecvState = 1;
    nw_browse_descriptor_t bd = nw_browse_descriptor_create_bonjour_service(kService, nullptr);
    nw_parameters_t params = nw_parameters_create();
    gBrowser = nw_browser_create(bd, params);
    nw_browser_set_queue(gBrowser, Q());
    nw_browser_set_browse_results_changed_handler(gBrowser, ^(nw_browse_result_t oldR, nw_browse_result_t newR, bool batchComplete) {
        if (newR != nullptr && gConn == nullptr) Connect(nw_browse_result_copy_endpoint(newR));
    });
    nw_browser_start(gBrowser);
    // Hello twice a second: the Mac sends only to devices it has heard from; silence for 3 s = reconnect.
    gHello = dispatch_source_create(DISPATCH_SOURCE_TYPE_TIMER, 0, 0, Q());
    dispatch_source_set_timer(gHello, dispatch_time(DISPATCH_TIME_NOW, 0), (uint64_t)(0.5 * NSEC_PER_SEC), (uint64_t)(0.05 * NSEC_PER_SEC));
    dispatch_source_set_event_handler(gHello, ^{
        if (gConn == nullptr) return;
        if (gRecvState == 2 && Now() - gLatestAt > 3.0) { Drop(); return; }   // (the browser finds it again)
        static const char hi[] = "AJ1H";
        dispatch_data_t d = dispatch_data_create(hi, 4, nullptr, DISPATCH_DATA_DESTRUCTOR_DEFAULT);
        nw_connection_send(gConn, d, NW_CONNECTION_DEFAULT_MESSAGE_CONTEXT, true, ^(nw_error_t e) {});
    });
    dispatch_resume(gHello);
}

extern "C" void JL_RecvStop()
{
    if (gBrowser == nullptr) return;
    nw_browser_t b = gBrowser; gBrowser = nullptr;
    dispatch_async(Q(), ^{
        if (gHello) { dispatch_source_cancel(gHello); gHello = nullptr; }
        Drop(); nw_browser_cancel(b); gRecvState = 0; gLatestLen = 0;
    });
}

/// 0 off · 1 searching · 2 connected. Copies the latest channel packet into buf (returns its length, 0 = none) and its
/// age in seconds; the Mac's name into name.
extern "C" int JL_Recv(unsigned char *buf, int max, float *age, char *name, int nameMax)
{
    __block int len = 0, st = 0;
    dispatch_sync(Q(), ^{
        st = gRecvState;
        if (gLatestLen > 0 && gLatestLen <= max) { memcpy(buf, gLatest, gLatestLen); len = gLatestLen; }
        if (age) *age = gLatestLen > 0 ? (float)(Now() - gLatestAt) : 999.0f;
        if (name && nameMax > 0) strlcpy(name, gPeerName, nameMax);
    });
    return len > 0 ? len : -st;   // (≤ 0: no packet; −state)
}

/// The Mac's calibration packet ("AJ1P" + JSON), once; returns its length (0 = none since the last call).
extern "C" int JL_RecvProfile(unsigned char *buf, int max)
{
    __block int len = 0;
    dispatch_sync(Q(), ^{ if (gLatestProfLen > 0 && gLatestProfLen <= max) { memcpy(buf, gLatestProf, gLatestProfLen); len = gLatestProfLen; gLatestProfLen = 0; } });
    return len;
}

extern "C" int JL_RecvState() { return gRecvState; }

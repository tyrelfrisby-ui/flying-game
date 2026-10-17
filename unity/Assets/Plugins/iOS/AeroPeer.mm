// FLYING WITH NO ROUTER AT ALL (owner 2026-10-17: "then the two ipads in a field"). Apple's peer-to-peer wifi,
// through MultipeerConnectivity: the devices make their own link out of nothing — no access point, no DHCP, no
// internet, and no addresses to type. Range is tens of metres and the latency is a single hop.
//
// Only the transport is here; everything above it is PeerLink.cs, which turns what this reports into the same
// messages the relay sends. The same source builds into the Mac bundle (tools/native-mac/build.sh), so a Mac
// can be in a field group too.
//
// Events are handed to C# as one string per event, fields separated by 0x1F (unit separator) because a peer's
// display name has its own structure and '|' in it:
//     <kind>\x1F<displayName>\x1F<payload>      kind: p = joined, b = gone, s = packet
#import <Foundation/Foundation.h>
#import <MultipeerConnectivity/MultipeerConnectivity.h>
#include <string.h>

// MCNearbyServiceBrowser's type: 1–15 characters of a–z, 0–9 and '-'. Everyone in a field group shares it;
// which ROOM they are in travels in the discovery info, so two groups in one field do not merge.
static NSString *const kService = @"aeroplay";
static const char kSep = 0x1F;

@interface AeroPeer : NSObject <MCSessionDelegate, MCNearbyServiceAdvertiserDelegate, MCNearbyServiceBrowserDelegate>
@property (nonatomic, strong) MCPeerID *me;
@property (nonatomic, strong) MCSession *session;
@property (nonatomic, strong) MCNearbyServiceAdvertiser *advertiser;
@property (nonatomic, strong) MCNearbyServiceBrowser *browser;
@property (nonatomic, strong) NSString *room;
@property (nonatomic, strong) NSMutableArray<NSString *> *events;
@property (nonatomic, strong) NSLock *lock;
@end

static AeroPeer *gPeer;

@implementation AeroPeer

- (instancetype)initWithRoom:(NSString *)room display:(NSString *)display
{
    if ((self = [super init]))
    {
        _room = room ?: @"LAN";
        _events = [NSMutableArray array];
        _lock = [[NSLock alloc] init];
        // MCPeerID's display name is limited to 63 bytes, and it is how the other side learns who we are.
        // MCPeerID THROWS on a name it does not like rather than returning nil — empty, or over 63 bytes —
        // so it is checked here as well as being guarded by @try at the call.
        NSString *d = display.length > 0 ? display : @"pilot";
        while ([d lengthOfBytesUsingEncoding:NSUTF8StringEncoding] > 63 && d.length > 1)
            d = [d substringToIndex:d.length - 1];
        if (d.length == 0) d = @"pilot";
        _me = [[MCPeerID alloc] initWithDisplayName:d];
        _session = [[MCSession alloc] initWithPeer:_me securityIdentity:nil encryptionPreference:MCEncryptionOptional];
        _session.delegate = self;
        _advertiser = [[MCNearbyServiceAdvertiser alloc] initWithPeer:_me
                                                        discoveryInfo:@{ @"room": _room }
                                                          serviceType:kService];
        _advertiser.delegate = self;
        _browser = [[MCNearbyServiceBrowser alloc] initWithPeer:_me serviceType:kService];
        _browser.delegate = self;
    }
    return self;
}

- (void)start
{
    [self.advertiser startAdvertisingPeer];
    [self.browser startBrowsingForPeers];
}

- (void)stop
{
    [self.advertiser stopAdvertisingPeer];
    [self.browser stopBrowsingForPeers];
    [self.session disconnect];
}

- (void)push:(NSString *)kind peer:(NSString *)display payload:(NSString *)payload
{
    NSString *e = [NSString stringWithFormat:@"%@%c%@%c%@", kind, kSep, display ?: @"", kSep, payload ?: @""];
    [self.lock lock];
    // A queue that nobody is draining must not grow for ever (the app went to the background).
    if (self.events.count < 2048) [self.events addObject:e];
    [self.lock unlock];
}

- (NSString *)takeEvent
{
    [self.lock lock];
    NSString *e = self.events.firstObject;
    if (e != nil) [self.events removeObjectAtIndex:0];
    [self.lock unlock];
    return e;
}

// ---- browsing. Both sides see each other, so only one of them should invite: the one whose name sorts
// first. Otherwise two invitations cross and MCSession drops one of the connections.
- (void)browser:(MCNearbyServiceBrowser *)browser foundPeer:(MCPeerID *)peerID withDiscoveryInfo:(NSDictionary<NSString *,NSString *> *)info
{
    NSString *theirRoom = info[@"room"];
    if (theirRoom != nil && ![theirRoom isEqualToString:self.room]) return;      // another group in the same field
    for (MCPeerID *p in self.session.connectedPeers) if ([p isEqual:peerID]) return;
    if ([self.me.displayName compare:peerID.displayName] == NSOrderedAscending)
        [browser invitePeer:peerID toSession:self.session withContext:[self.room dataUsingEncoding:NSUTF8StringEncoding] timeout:20];
}

- (void)browser:(MCNearbyServiceBrowser *)browser lostPeer:(MCPeerID *)peerID { }

- (void)browser:(MCNearbyServiceBrowser *)browser didNotStartBrowsingForPeers:(NSError *)error
{
    [self push:@"e" peer:@"" payload:error.localizedDescription];
}

// ---- advertising
- (void)advertiser:(MCNearbyServiceAdvertiser *)advertiser
didReceiveInvitationFromPeer:(MCPeerID *)peerID
       withContext:(NSData *)context
 invitationHandler:(void (^)(BOOL, MCSession * _Nullable))handler
{
    NSString *theirRoom = context != nil ? [[NSString alloc] initWithData:context encoding:NSUTF8StringEncoding] : nil;
    BOOL ours = theirRoom == nil || [theirRoom isEqualToString:self.room];
    handler(ours, ours ? self.session : nil);
}

- (void)advertiser:(MCNearbyServiceAdvertiser *)advertiser didNotStartAdvertisingPeer:(NSError *)error
{
    [self push:@"e" peer:@"" payload:error.localizedDescription];
}

// ---- the session
- (void)session:(MCSession *)session peer:(MCPeerID *)peerID didChangeState:(MCSessionState)state
{
    if (state == MCSessionStateConnected) [self push:@"p" peer:peerID.displayName payload:@""];
    else if (state == MCSessionStateNotConnected) [self push:@"b" peer:peerID.displayName payload:@""];
}

- (void)session:(MCSession *)session didReceiveData:(NSData *)data fromPeer:(MCPeerID *)peerID
{
    NSString *s = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
    if (s != nil) [self push:@"s" peer:peerID.displayName payload:s];
}

- (void)session:(MCSession *)session didReceiveCertificate:(NSArray *)certificate fromPeer:(MCPeerID *)peerID certificateHandler:(void (^)(BOOL))handler
{
    handler(YES);    // a field group has no certificate authority in it; the encryption is for the link only
}

// Required, unused: this carries nothing but data.
- (void)session:(MCSession *)session didReceiveStream:(NSInputStream *)stream withName:(NSString *)streamName fromPeer:(MCPeerID *)peerID { }
- (void)session:(MCSession *)session didStartReceivingResourceWithName:(NSString *)resourceName fromPeer:(MCPeerID *)peerID withProgress:(NSProgress *)progress { }
- (void)session:(MCSession *)session didFinishReceivingResourceWithName:(NSString *)resourceName fromPeer:(MCPeerID *)peerID atURL:(NSURL *)localURL withError:(NSError *)error { }

@end

// ---- the C face

extern "C" int AP_Available() { return 1; }

extern "C" int AP_Start(const char *room, const char *display)
{
    if (gPeer != nil) return 1;
    @try
    {
        gPeer = [[AeroPeer alloc] initWithRoom:room ? [NSString stringWithUTF8String:room] : nil
                                       display:display ? [NSString stringWithUTF8String:display] : nil];
        [gPeer start];
    }
    @catch (NSException *e) { gPeer = nil; return 0; }
    return gPeer != nil ? 1 : 0;
}

extern "C" void AP_Stop()
{
    if (gPeer == nil) return;
    [gPeer stop];
    gPeer = nil;
}

extern "C" int AP_Count() { return gPeer != nil ? (int)gPeer.session.connectedPeers.count : 0; }

/// <summary>Our own name changed. MCPeerID is immutable, so this is a stop and a start — the group re-forms.</summary>
extern "C" void AP_SetDisplay(const char *display)
{
    if (gPeer == nil || display == nullptr) return;
    NSString *d = [NSString stringWithUTF8String:display];
    if ([d isEqualToString:gPeer.me.displayName]) return;
    NSString *room = gPeer.room;
    AP_Stop();
    AP_Start(room.UTF8String, d.UTF8String);
}

extern "C" void AP_Send(const char *utf8, int len, int reliable)
{
    if (gPeer == nil || utf8 == nullptr || len <= 0) return;
    NSArray<MCPeerID *> *peers = gPeer.session.connectedPeers;
    if (peers.count == 0) return;
    NSData *d = [NSData dataWithBytes:utf8 length:(NSUInteger)len];
    // Unreliable for state: a late position is worth less than the next one, and head-of-line blocking on a
    // weak link is what makes an aeroplane stutter. Reliable for the radio, which must not lose a word.
    [gPeer.session sendData:d toPeers:peers
                   withMode:reliable ? MCSessionSendDataReliable : MCSessionSendDataUnreliable
                      error:nil];
}

/// Copies one event into the caller's buffer, NUL-terminated. Returns its length, or 0 when there is nothing.
extern "C" int AP_Poll(char *buf, int max)
{
    if (gPeer == nil || buf == nullptr || max <= 1) return 0;
    NSString *e = [gPeer takeEvent];
    if (e == nil) return 0;
    const char *s = e.UTF8String;
    int n = (int)strlen(s);
    if (n > max - 1) n = max - 1;
    memcpy(buf, s, (size_t)n);
    buf[n] = 0;
    return n;
}

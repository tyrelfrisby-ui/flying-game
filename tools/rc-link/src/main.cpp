// AERO RC LINK -- the firmware. Fly Aero Playground on the iPad or iPhone from a real RC transmitter.
//
//   transmitter trainer port --(PPM or SBUS)--> ESP32 --(Wi-Fi: JoyLink)--> iPad / iPhone / Mac
//                                                    \-(BLE HID gamepad)-> Android / Mac / Windows / PC sims
//
// ===============================================================================================================
// UNTESTED. Nobody has flashed this. Nobody has the board in hand.
// ===============================================================================================================
// Everything in this file touches a pin, a radio or a Bluetooth stack and has NEVER BEEN RUN. What HAS been run is
// the pure half -- the decoders, the calibration, the failsafe, the mapping, the packet bytes and the throttle
// servo -- which lives in include/*.h and is exercised by test/host_test.cpp on the Mac with clang (199 checks).
// The split is deliberate: every decision with a right and a wrong answer is in the tested half, and what is left
// here is wiring, which fails loudly on the first power-up rather than subtly in the air. README.md's first
// section is a two-minute test that says WHICH HALF is wrong before any flying happens.
//
// ===============================================================================================================
// WHAT THE OWNER ASKED FOR, AND THE ONE PLACE THIS DOES NOT DO IT
// ===============================================================================================================
// Ty, 2026-11-10, choosing the shape: "The board sits on the transmitter's trainer port, decodes the channel
// stream and presents itself as a BLE gamepad. The phone needs no special code -- it sees a standard controller."
//
// The trainer port and the decode are exactly as asked. The BLE gamepad is built and is here (RC_BLE). But the
// last sentence does not hold on iOS, and the evidence was already in this repo before today:
// docs/RC-LINK-SPEC.md, written in October for the previous shape, says "GameController only accepts Xbox,
// PlayStation, Switch Pro and MFi controllers", and Unity on iOS reads pads through that framework and nothing
// else. A home-made BLE HID gamepad PAIRS with an iPhone and is then invisible to every app. See README.md.
//
// So the DEFAULT transport is JoyLink over Wi-Fi, which is not a new protocol either -- it is the one the game
// already ships (JoyLink.cs + Plugins/iOS/JoyLink.mm), already declared in NSBonjourServices, already wired to
// the calibration wizard that was written for an RC radio in the first place. Nothing in unity/ changes for
// either transport. Both can run at once, which is how he finds out in thirty seconds which claim is true.

#include <Arduino.h>
#include <Preferences.h>
#include <WiFi.h>
#include <WiFiUdp.h>
#include <WebServer.h>
#include <ESPmDNS.h>

#include "../include/rc_decode.h"
#include "../include/rc_map.h"
#include "../include/joylink_packet.h"

using namespace rclink;

// ---------------------------------------------------------------------------------------------------------------
// BUILD CONFIGURATION -- all of it overridable from platformio.ini's build_flags, none of it requiring an edit
// to a tracked file (this is a PUBLIC repo: no Wi-Fi credentials are compiled in, see provisioning below).
// ---------------------------------------------------------------------------------------------------------------

#ifndef RC_PIN            // the trainer-port signal. Per-board defaults are in platformio.ini.
#define RC_PIN 32
#endif
#ifndef RC_BUTTON_PIN     // a button to hold at power-up; -1 for a board without one.
#define RC_BUTTON_PIN -1
#endif
#ifndef RC_BUTTON_ACTIVE_LOW
#define RC_BUTTON_ACTIVE_LOW 1
#endif
#ifndef RC_LED_PIN        // a plain LED; -1 for none. Deliberately NOT a WS2812 or a display: every M5 variant
#define RC_LED_PIN -1     // wires those differently, and the status that matters is on the serial line, which
#endif                    // every board has.
#ifndef RC_JOYLINK        // the shipping path: Wi-Fi + Bonjour + UDP. iOS, iPadOS, macOS.
#define RC_JOYLINK 1
#endif
#ifndef RC_BLE            // what the owner asked for. Android, Mac, Windows; NOT iOS (see above).
#define RC_BLE 0
#endif
#ifndef RC_ORDER          // AETR (FrSky/Futaba/EdgeTX), TAER (Spektrum/JR), RETA (older Hitec/Multiplex).
#define RC_ORDER AETR
#endif
#ifndef RC_FLAP_CHANNEL   // the 2- or 3-position switch that becomes the flap lever; 0 for none.
#define RC_FLAP_CHANNEL 5
#endif
#ifndef RC_AP_SSID        // the board's own access point, for provisioning and for flying away from a router.
#define RC_AP_SSID "Aero-RC-Link"
#endif

static constexpr int kPin = RC_PIN;
static constexpr int kButtonPin = RC_BUTTON_PIN;
static constexpr int kLedPin = RC_LED_PIN;
static constexpr uint32_t kSendHz = 100;      // JoyLink.cs's own sender rate ceiling
static constexpr uint32_t kProfileMs = 1000;  // ...and its profile rate
static constexpr uint32_t kPeerStaleMs = 3000;// JoyLink.mm: the Mac sends only to peers heard from in 3 s

// ---------------------------------------------------------------------------------------------------------------
// STATE
// ---------------------------------------------------------------------------------------------------------------

static PpmDecoder     gPpm;
static SbusDecoder    gSbus;
static FormatDetector gDetect;
static Failsafe       gFailsafe;
static ThrottleServo  gServo;
static Cal            gCal[kMaxChannels];
static FunctionChannels gFunc = FunctionChannels::of(Order::RC_ORDER);
static Preferences    gNvs;

static Frame    gFrame;          // the last frame decoded, whatever the format
static uint32_t gFrameCount;     // frames that reached the mapping (for the status line)
static Link     gLinkWas = Link::Dead;
static Format   gFormatWas = Format::None;

// The PPM edge queue. The ISR must do nothing but stamp and store; everything else is in the loop.
static constexpr uint16_t kEdgeCap = 128;     // ~7 PPM frames of headroom
static volatile uint32_t  gEdges[kEdgeCap];
static volatile uint16_t  gEdgeHead = 0, gEdgeTail = 0;
static volatile uint32_t  gEdgeDropped = 0;

static void IRAM_ATTR onEdge()
{
    uint32_t t = micros();
    uint16_t h = gEdgeHead, n = uint16_t((h + 1) % kEdgeCap);
    if (n == gEdgeTail) { gEdgeDropped++; return; }   // the loop is behind: drop, never block in an ISR
    gEdges[h] = t;
    gEdgeHead = n;
}

static HardwareSerial gRcSerial(1);
static bool gSerialUp = false, gIsrUp = false;

/// Point the pin at the UART (the SBUS phase). See the FormatDetector comment in rc_decode.h for why the two
/// phases are exclusive rather than simultaneous.
static void listenSbus()
{
    if (gIsrUp) { detachInterrupt(digitalPinToInterrupt(kPin)); gIsrUp = false; }
    if (!gSerialUp)
    {
        // 100000 baud, 8 data bits, EVEN parity, 2 stop bits, and INVERTED -- Futaba's SBUS exactly. The
        // inversion is done in the ESP32's own UART (the `invert` argument), which is why no hardware inverter
        // is needed on the lead; that is worth knowing before anyone solders a transistor onto it.
        gRcSerial.begin(100000, SERIAL_8E2, kPin, -1, true);
        gSerialUp = true;
    }
    gSbus.reset();
}

/// Point the pin at the edge interrupt (the PPM phase).
static void listenPpm()
{
    if (gSerialUp) { gRcSerial.end(); gSerialUp = false; }
    if (!gIsrUp)
    {
        pinMode(kPin, INPUT);
        // RISING only. Polarity does not matter for PPM -- rising-to-rising and falling-to-falling intervals come
        // out identical, which is derived in rc_decode.h and tested in host_test.cpp -- so one polarity serves
        // both positive and negative trainer ports and the owner never has to know which his radio puts out.
        attachInterrupt(digitalPinToInterrupt(kPin), onEdge, RISING);
        gIsrUp = true;
    }
    gPpm.reset();
    gEdgeTail = gEdgeHead;
}

static void applyListening()
{
    switch (gDetect.listening())
    {
        case Format::Sbus: listenSbus(); break;
        case Format::Ppm:  listenPpm();  break;
        default: break;
    }
}

// ---------------------------------------------------------------------------------------------------------------
// CALIBRATION IN NVS
// ---------------------------------------------------------------------------------------------------------------
// Only the BLE gamepad path needs this. On the JoyLink path the game's own wizard measures the travel and stores
// it per device name, and a second copy on the board would be a second place for the elevator's travel to be
// remembered and a guarantee that one day the two disagree (see the CALIBRATION comment in rc_map.h).

static void loadCal()
{
    gNvs.begin("rclink", true);
    size_t want = sizeof(Cal) * kMaxChannels;
    if (gNvs.getBytesLength("cal") == want) gNvs.getBytes("cal", gCal, want);
    gNvs.end();
}

static void saveCal()
{
    gNvs.begin("rclink", false);
    gNvs.putBytes("cal", gCal, sizeof(Cal) * kMaxChannels);
    gNvs.end();
}

static CalSweep gSweep;
static bool     gSweeping = false;
static uint32_t gSweepUntilMs = 0;

static void startSweep()
{
    Serial.println("[cal] SWEEP: move every stick to both stops and flip every switch both ways. 12 s.");
    Serial.println("[cal] then LET GO of everything and keep still for the last 3 s.");
    gSweep.begin();
    gSweeping = true;
    gSweepUntilMs = millis() + 15000;
}

static void tickSweep(uint32_t nowMs)
{
    if (!gSweeping) return;
    uint32_t left = gSweepUntilMs > nowMs ? gSweepUntilMs - nowMs : 0;
    if (left > 3000) { gSweep.feed(gFrame); return; }   // the last 3 s are the rest reading, not the sweep
    if (left > 0) return;
    gSweeping = false;
    uint8_t good = gSweep.finish(gFrame, gCal);
    if (good == 0) { Serial.println("[cal] nothing moved -- calibration NOT saved, the old one stands."); return; }
    saveCal();
    Serial.printf("[cal] saved: %u channel(s) calibrated.\n", good);
    for (uint8_t c = 0; c < 10; c++)
        Serial.printf("[cal]   ch%-2u  %4u .. %4u us, centre %4u\n", c + 1, gCal[c].lo, gCal[c].hi, gCal[c].mid);
}

// ---------------------------------------------------------------------------------------------------------------
// WI-FI
// ---------------------------------------------------------------------------------------------------------------
// Two ways on, and NO CREDENTIALS IN THE REPO -- this repo is public, so they live in NVS and are typed in once
// through the board's own access point.
//
//   STATION (default once provisioned) -- the board joins his Wi-Fi and the iPad finds it over Bonjour, exactly
//     as the iPad finds the Mac today. Same network, same subnet; Bonjour will not cross a guest network or a
//     router with mDNS filtering, which is the first thing to suspect if the board is up and the iPad sees
//     nothing.
//   ACCESS POINT (hold the button at power-up, or no credentials stored yet) -- the board IS the network. The
//     iPad joins "Aero-RC-Link" and Bonjour works over it with no router involved at all, which is the mode for
//     flying somewhere without his Wi-Fi. While in AP mode with no credentials stored, http://192.168.4.1 serves
//     a two-field form that saves them.

static WebServer gWeb(80);
static bool gApMode = false, gProvisioning = false;

static bool buttonHeld()
{
    if (kButtonPin < 0) return false;
    int v = digitalRead(kButtonPin);
    return RC_BUTTON_ACTIVE_LOW ? v == LOW : v == HIGH;
}

static void serveForm()
{
    gWeb.send(200, "text/html",
        "<!doctype html><meta name=viewport content='width=device-width,initial-scale=1'>"
        "<title>Aero RC Link</title>"
        "<style>body{font:17px -apple-system,sans-serif;margin:2em;max-width:26em}"
        "input{font:inherit;width:100%;padding:.5em;margin:.3em 0 1em;box-sizing:border-box}"
        "button{font:inherit;padding:.6em 1.4em}</style>"
        "<h2>Aero RC Link</h2><p>The Wi-Fi the board should join. Stored on the board only.</p>"
        "<form method=post action=/save><label>Network</label><input name=s autocapitalize=off>"
        "<label>Password</label><input name=p type=password>"
        "<button>Save and restart</button></form>");
}

static void serveSave()
{
    String s = gWeb.arg("s"), p = gWeb.arg("p");
    if (s.length() == 0) { gWeb.send(400, "text/plain", "No network name."); return; }
    gNvs.begin("rclink", false);
    gNvs.putString("ssid", s);
    gNvs.putString("pass", p);
    gNvs.end();
    gWeb.send(200, "text/html", "<body style='font:17px sans-serif;margin:2em'>Saved. Restarting \xE2\x80\x94 "
                                "rejoin your own Wi-Fi on this phone.</body>");
    delay(600);
    ESP.restart();
}

static void startAp(bool provisioning)
{
    gApMode = true;
    gProvisioning = provisioning;
    WiFi.mode(WIFI_AP);
    // Open, on purpose: it carries stick positions on a field and nothing else, and a WPA key he has to remember
    // is one more thing between him and flying. If that is ever not wanted, put a password in the third argument.
    WiFi.softAP(RC_AP_SSID);
    Serial.printf("[wifi] access point \"%s\" up at %s\n", RC_AP_SSID, WiFi.softAPIP().toString().c_str());
    if (provisioning)
    {
        gWeb.on("/", serveForm);
        gWeb.on("/save", HTTP_POST, serveSave);
        gWeb.onNotFound(serveForm);
        gWeb.begin();
        Serial.println("[wifi] no network stored: join that AP and open http://192.168.4.1 to set one.");
    }
}

static void startWifi()
{
    if (buttonHeld())
    {
        Serial.println("[wifi] button held at power-up: access-point mode.");
        startAp(false);
        return;
    }
    gNvs.begin("rclink", true);
    String ssid = gNvs.getString("ssid", ""), pass = gNvs.getString("pass", "");
    gNvs.end();
    if (ssid.length() == 0) { startAp(true); return; }

    WiFi.mode(WIFI_STA);
    WiFi.setSleep(false);   // Wi-Fi power save adds up to 100 ms of latency to a 10 ms control stream
    WiFi.begin(ssid.c_str(), pass.c_str());
    Serial.printf("[wifi] joining \"%s\"", ssid.c_str());
    for (int i = 0; i < 60 && WiFi.status() != WL_CONNECTED; i++) { delay(250); Serial.print('.'); }
    Serial.println();
    if (WiFi.status() != WL_CONNECTED)
    {
        Serial.println("[wifi] could not join -- falling back to access-point mode so the iPad can still reach it.");
        startAp(false);
        return;
    }
    Serial.printf("[wifi] %s as %s\n", ssid.c_str(), WiFi.localIP().toString().c_str());
}

// ---------------------------------------------------------------------------------------------------------------
// JOYLINK -- Bonjour + UDP, standing in for the Mac
// ---------------------------------------------------------------------------------------------------------------

#if RC_JOYLINK
static WiFiUDP gUdp;
static uint16_t gSeq = 0;

struct JoyPeer { IPAddress ip; uint16_t port = 0; uint32_t heardMs = 0; };
static JoyPeer gPeers[4];
static uint8_t gPeerCount = 0;

static void startJoyLink()
{
    gUdp.begin(kUdpPort);
    if (!MDNS.begin("aero-rc-link")) { Serial.println("[joylink] mDNS failed to start"); return; }
    // The INSTANCE name is what the iPad shows: JoyLink.mm reads it with nw_endpoint_get_bonjour_service_name and
    // JoyLink.cs prints it in OPTIONS as "<joystick> via <that name>".
    MDNS.setInstanceName(kDeviceName);
    MDNS.addService(kBonjourService, "udp", kUdpPort);
    Serial.printf("[joylink] _%s._udp \"%s\" on port %u\n", kBonjourService, kDeviceName, kUdpPort);
}

/// The iPad says "AJ1H" twice a second and the board replies to wherever that came from. Exactly what the Mac
/// does: no peer, no packets, so an idle board is off the air.
static void readHellos(uint32_t nowMs)
{
    uint8_t buf[64];
    int n;
    while ((n = gUdp.parsePacket()) > 0)
    {
        int got = gUdp.read(buf, sizeof buf);
        if (got <= 0 || !isHello(buf, size_t(got))) continue;
        IPAddress ip = gUdp.remoteIP();
        uint16_t port = gUdp.remotePort();
        bool known = false;
        for (uint8_t i = 0; i < gPeerCount; i++)
            if (gPeers[i].ip == ip && gPeers[i].port == port) { gPeers[i].heardMs = nowMs; known = true; break; }
        if (known) continue;
        // A new device. Take a free slot, or the stalest one.
        uint8_t slot = gPeerCount;
        if (gPeerCount < 4) gPeerCount++;
        else { slot = 0; for (uint8_t i = 1; i < 4; i++) if (gPeers[i].heardMs < gPeers[slot].heardMs) slot = i; }
        gPeers[slot] = { ip, port, nowMs };
        Serial.printf("[joylink] device %s:%u is listening\n", ip.toString().c_str(), port);
    }
}

static uint8_t livePeers(uint32_t nowMs)
{
    uint8_t n = 0;
    for (uint8_t i = 0; i < gPeerCount; i++) if (nowMs - gPeers[i].heardMs < kPeerStaleMs) n++;
    return n;
}

static void sendToPeers(uint32_t nowMs, const uint8_t* pkt, size_t len)
{
    for (uint8_t i = 0; i < gPeerCount; i++)
    {
        if (nowMs - gPeers[i].heardMs >= kPeerStaleMs) continue;
        gUdp.beginPacket(gPeers[i].ip, gPeers[i].port);
        gUdp.write(pkt, len);
        gUdp.endPacket();
    }
}

static void tickJoyLink(uint32_t nowMs, const JoyLinkOut& out)
{
    readHellos(nowMs);
    if (livePeers(nowMs) == 0) return;

    static uint32_t lastSendMs = 0, lastProfileMs = 0;
    uint8_t pkt[kMaxPacket];
    if (nowMs - lastSendMs >= 1000 / kSendHz)
    {
        lastSendMs = nowMs;
        size_t n = buildChannelPacket(pkt, sizeof pkt, gSeq++, out.axes, out.buttons, kDeviceName);
        if (n) sendToPeers(nowMs, pkt, n);
    }
    if (nowMs - lastProfileMs >= kProfileMs)
    {
        lastProfileMs = nowMs;
        // The first-flight profile. The iPad adopts it ONLY if it has none of its own for this device name, so the
        // moment he runs OPTIONS > JOYSTICK SETUP his own calibration wins for good. See joylink_packet.h.
        size_t n = buildProfilePacket(pkt, sizeof pkt, gFunc.aileron, gFunc.elevator, gFunc.throttle, gFunc.rudder,
                                      RC_FLAP_CHANNEL);
        if (n) sendToPeers(nowMs, pkt, n);
    }
}
#endif   // RC_JOYLINK

// ---------------------------------------------------------------------------------------------------------------
// BLE HID GAMEPAD -- what the owner asked for
// ---------------------------------------------------------------------------------------------------------------
// A plain HID-over-GATT gamepad: 4 x int16 axes, 2 x uint8 triggers, 16 buttons. No third-party library, so the
// default build cannot fail because a library moved; the BLE stack is the one bundled with the ESP32 Arduino core.
//
// Expected to work on Android, Windows, macOS and any PC sim, and expected NOT to be seen by iOS -- see the note
// at the top of this file and the sources in README.md. It is here so that claim can be falsified in thirty
// seconds with the hardware, instead of taken on trust from someone who does not have it.

#if RC_BLE
#include <BLEDevice.h>
#include <BLEHIDDevice.h>
#include <BLEServer.h>

static BLEHIDDevice*    gHid = nullptr;
static BLECharacteristic* gInput = nullptr;
static volatile bool    gBleConnected = false;

static const uint8_t kReportMap[] = {
    0x05, 0x01,              // Usage Page (Generic Desktop)
    0x09, 0x05,              // Usage (Game Pad)
    0xA1, 0x01,              // Collection (Application)
    0x85, 0x01,              //   Report ID (1)
    0xA1, 0x00,              //   Collection (Physical)
    0x05, 0x01,              //     Usage Page (Generic Desktop)
    0x09, 0x30,              //     Usage (X)   -- left stick X   : RUDDER
    0x09, 0x31,              //     Usage (Y)   -- left stick Y   : THROTTLE (as a rate; see ThrottleServo)
    0x09, 0x32,              //     Usage (Z)   -- right stick X  : ROLL
    0x09, 0x35,              //     Usage (Rz)  -- right stick Y  : PITCH
    0x16, 0x01, 0x80,        //     Logical Minimum (-32767)
    0x26, 0xFF, 0x7F,        //     Logical Maximum (32767)
    0x75, 0x10,              //     Report Size (16)
    0x95, 0x04,              //     Report Count (4)
    0x81, 0x02,              //     Input (Data, Var, Abs)
    0x05, 0x02,              //     Usage Page (Simulation Controls)
    0x09, 0xC5,              //     Usage (Brake)        -- left trigger
    0x09, 0xC4,              //     Usage (Accelerator)  -- right trigger
    0x15, 0x00,              //     Logical Minimum (0)
    0x26, 0xFF, 0x00,        //     Logical Maximum (255)
    0x75, 0x08,              //     Report Size (8)
    0x95, 0x02,              //     Report Count (2)
    0x81, 0x02,              //     Input (Data, Var, Abs)
    0xC0,                    //   End Collection
    0x05, 0x09,              //   Usage Page (Button)
    0x19, 0x01,              //   Usage Minimum (Button 1)
    0x29, 0x10,              //   Usage Maximum (Button 16)
    0x15, 0x00, 0x25, 0x01,  //   Logical 0..1
    0x75, 0x01,              //   Report Size (1)
    0x95, 0x10,              //   Report Count (16)
    0x81, 0x02,              //   Input (Data, Var, Abs)
    0xC0                     // End Collection
};

class BleCallbacks : public BLEServerCallbacks
{
    void onConnect(BLEServer*) override { gBleConnected = true; Serial.println("[ble] a host connected"); }
    void onDisconnect(BLEServer*) override
    {
        gBleConnected = false;
        Serial.println("[ble] the host went away; advertising again");
        BLEDevice::startAdvertising();
    }
};

static void startBle()
{
    BLEDevice::init(kDeviceName);
    BLEServer* server = BLEDevice::createServer();
    server->setCallbacks(new BleCallbacks());
    gHid = new BLEHIDDevice(server);
    gInput = gHid->inputReport(1);
    gHid->manufacturer()->setValue("Aero Playground");
    // PnP: a generic USB-IF vendor id. NOT a spoof of a console pad's VID/PID -- that was considered and rejected
    // (README.md, "What was rejected"): iOS does not key its controller support off the VID alone, pretending to
    // be hardware that answers a proprietary handshake is a dead end, and the -S3 and -C3 parts cannot do the
    // Bluetooth Classic that the real Xbox pad uses anyway.
    gHid->pnp(0x02, 0xE502, 0xA111, 0x0210);
    gHid->hidInfo(0x00, 0x01);
    gHid->reportMap(const_cast<uint8_t*>(kReportMap), sizeof kReportMap);
    gHid->startServices();
    BLEDevice::setSecurityAuth(true, false, true);   // bonded, no MITM, SC -- what a gamepad normally asks for
    BLEAdvertising* adv = BLEDevice::getAdvertising();
    adv->setAppearance(0x03C4);                      // Bluetooth SIG appearance: HID Gamepad
    adv->addServiceUUID(gHid->hidService()->getUUID());
    adv->start();
    gHid->setBatteryLevel(100);
    Serial.printf("[ble] advertising as \"%s\" (HID gamepad)\n", kDeviceName);
}

static void tickBle(const GamepadOut& g)
{
    if (!gBleConnected || gInput == nullptr) return;
    uint8_t r[12];
    putU16(r + 0, uint16_t(g.lx));
    putU16(r + 2, uint16_t(g.ly));
    putU16(r + 4, uint16_t(g.rx));
    putU16(r + 6, uint16_t(g.ry));
    r[8] = g.lt;
    r[9] = g.rt;
    putU16(r + 10, g.buttons);
    gInput->setValue(r, sizeof r);
    gInput->notify();
}
#endif   // RC_BLE

// ---------------------------------------------------------------------------------------------------------------
// THE STATUS LINE -- the fast-fail test
// ---------------------------------------------------------------------------------------------------------------
// README.md's first section is: power the board, open the serial monitor, and read this line. It answers the one
// question worth answering before any flying -- WHICH HALF IS WRONG:
//
//   format none           -> the BOARD is not seeing the transmitter. Lead, pin, trainer port, transmitter mode.
//   format SBUS/PPM + us  -> the board sees the channels. The radio half is fine.
//   joylink 0 devices     -> the PHONE is not reaching the board. Wi-Fi, subnet, the Options switch.
//   joylink 1 device      -> both halves are up, and anything still wrong is mapping or calibration.
// Neither half guesses at the other's problem, which is the whole point of printing them side by side.

static void printStatus(uint32_t nowMs)
{
    Format f = gDetect.format();
    Serial.printf("[rc] format %-4s  link %-4s  frames %-8u", formatName(f), linkName(gFailsafe.state()), gFrameCount);
    if (f != Format::None)
    {
        Serial.print("  ch");
        uint8_t n = gFrame.count < 8 ? gFrame.count : 8;
        for (uint8_t c = 0; c < n; c++) Serial.printf(" %4u", gFrame.us[c]);
        if (gFrame.failsafe) Serial.print("  FAILSAFE");
        if (gFrame.frameLost) Serial.print("  FRAME-LOST");
    }
    else
    {
        Serial.printf("  listening for %s on pin %d", formatName(gDetect.listening()), kPin);
    }
#if RC_JOYLINK
    Serial.printf("  | joylink %s %u device(s)",
                  gApMode ? RC_AP_SSID : WiFi.localIP().toString().c_str(), livePeers(nowMs));
#endif
#if RC_BLE
    Serial.printf("  | ble %s", gBleConnected ? "connected" : "advertising");
    Serial.printf("  lever %.2f", gServo.lever());
#endif
    if (gEdgeDropped) Serial.printf("  | %u edges dropped", gEdgeDropped);
    Serial.println();
    (void)nowMs;
}

// ---------------------------------------------------------------------------------------------------------------
// SETUP / LOOP
// ---------------------------------------------------------------------------------------------------------------

void setup()
{
    Serial.begin(115200);
    delay(300);
    Serial.println();
    Serial.println("Aero RC Link -- UNTESTED FIRMWARE. Read tools/rc-link/README.md before flying.");
    Serial.printf("  signal pin %d   channel order %s   flap switch ch%d\n", kPin, orderName(Order::RC_ORDER),
                  int(RC_FLAP_CHANNEL));

    if (kButtonPin >= 0) pinMode(kButtonPin, RC_BUTTON_ACTIVE_LOW ? INPUT_PULLUP : INPUT_PULLDOWN);
    if (kLedPin >= 0) { pinMode(kLedPin, OUTPUT); digitalWrite(kLedPin, LOW); }

    loadCal();
    startWifi();
#if RC_JOYLINK
    if (!gProvisioning) startJoyLink();
#endif
#if RC_BLE
    if (!gProvisioning) startBle();
#endif

    uint32_t now = millis();
    gDetect.begin(now);
    gFailsafe.begin(now);
    gServo.begin(0.f);
    applyListening();
}

void loop()
{
    uint32_t nowMs = millis();

    if (gProvisioning) { gWeb.handleClient(); return; }   // nothing else matters until it has a network

    // ---- drain whichever input is live -------------------------------------------------------------------
    if (gIsrUp)
    {
        while (gEdgeTail != gEdgeHead)
        {
            uint32_t t = gEdges[gEdgeTail];
            gEdgeTail = uint16_t((gEdgeTail + 1) % kEdgeCap);
            if (gPpm.feedEdge(t)) { gFrame = gPpm.frame(); gFrameCount++; gFailsafe.onFrame(nowMs, gFrame); }
        }
    }
    else if (gSerialUp)
    {
        static uint32_t lastByteUs = 0;
        if (gRcSerial.available() == 0 && lastByteUs && micros() - lastByteUs > 2000) { gSbus.gap(); lastByteUs = 0; }
        while (gRcSerial.available())
        {
            lastByteUs = micros();
            if (gSbus.feedByte(uint8_t(gRcSerial.read())))
            {
                gFrame = gSbus.frame();
                gFrameCount++;
                gFailsafe.onFrame(nowMs, gFrame);
            }
        }
    }

    // ---- format detection: re-wire the pin when the phase or the lock changes -----------------------------
    if (gDetect.update(nowMs, gPpm.frames(), gSbus.frames())) applyListening();
    if (gDetect.format() != gFormatWas)
    {
        gFormatWas = gDetect.format();
        Serial.printf("[rc] %s\n", gFormatWas == Format::None ? "lost the channel stream -- hunting again"
                                                              : formatName(gFormatWas));
        if (gFormatWas != Format::None) Serial.printf("[rc] locked %s\n", formatName(gFormatWas));
    }

    // ---- the four flight controls, then the failsafe decision ---------------------------------------------
    Link link = gFailsafe.update(nowMs);
    if (link != gLinkWas)
    {
        gLinkWas = link;
        Serial.printf("[rc] link %s%s\n", linkName(link),
                      link == Link::Lost ? " -- surfaces centred, throttle held"
                      : link == Link::Dead ? " -- surfaces centred, THROTTLE CLOSED" : "");
    }
    if (kLedPin >= 0) digitalWrite(kLedPin, link == Link::Live ? HIGH : (nowMs / 150) % 2);   // flash when not live

    tickSweep(nowMs);
    static uint32_t gHeldSince = 0;
    if (kButtonPin >= 0 && !gSweeping)
    {
        if (!buttonHeld()) gHeldSince = 0;
        else if (!gHeldSince) gHeldSince = nowMs;
        else if (nowMs - gHeldSince > 3000) { gHeldSince = 0; startSweep(); }
    }

    // ---- the failsafe, applied ONCE, to a copy of the frame that both transports then send ----------------
    // Both outputs must see the same aeroplane; applying the decision twice, or to one path only, is how a
    // dropout would centre the pad and not the JoyLink stream (or the other way about).
    float ail = gCal[gFunc.aileron - 1].bipolar(gFrame.us[gFunc.aileron - 1]);
    float ele = gCal[gFunc.elevator - 1].bipolar(gFrame.us[gFunc.elevator - 1]);
    float rud = gCal[gFunc.rudder - 1].bipolar(gFrame.us[gFunc.rudder - 1]);
    float thr = gCal[gFunc.throttle - 1].unipolar(gFrame.us[gFunc.throttle - 1]);
    gFailsafe.apply(ail, ele, rud, thr);

    Frame safe = gFrame;
    if (link != Link::Live)
    {
        // Centre the three surfaces and write back the held (or closed) throttle, in microseconds, so the rest of
        // the chain cannot tell a failsafe frame from a flown one and nothing downstream needs a special case.
        // The AUX channels keep their last values on purpose: a flap switch or a gear switch snapping on a 200 ms
        // Wi-Fi hiccup would be its own emergency.
        if (safe.count < kMinChannels) { safe.count = 8; for (uint8_t c = 0; c < 8; c++) safe.us[c] = 1500; }
        safe.us[gFunc.aileron - 1] = safe.us[gFunc.elevator - 1] = safe.us[gFunc.rudder - 1] = 1500;
        safe.us[gFunc.throttle - 1] = uint16_t(1000.f + thr * 1000.f);
    }

    // ---- out, both ways -----------------------------------------------------------------------------------
#if RC_JOYLINK
    // The JoyLink path sends the channels against the FIXED 1000/1500/2000 reference, not against gCal: the
    // game's own wizard measures the real travel and owns that calibration (see rc_map.h). Two calibrations for
    // one transmitter is one too many.
    static Cal fixedRef[kMaxChannels];   // defaults: 1000 / 1500 / 2000
    tickJoyLink(nowMs, JoyLinkOut::from(safe, fixedRef));
#endif
#if RC_BLE
    {
        static uint32_t lastUs = 0;
        uint32_t nowUs = micros();
        float dt = lastUs ? float(nowUs - lastUs) * 1e-6f : 0.f;
        lastUs = nowUs;
        // The gamepad path DOES use gCal, because a pad has no wizard behind it, and it takes the throttle from
        // the servo rather than from the channel -- see the ThrottleServo comment in rc_map.h for why.
        float stick = gServo.step(thr, dt);
        tickBle(GamepadOut::from(safe, gCal, gFunc, stick));
    }
#endif

    static uint32_t lastStatusMs = 0;
    if (nowMs - lastStatusMs >= 500) { lastStatusMs = nowMs; printStatus(nowMs); }
}

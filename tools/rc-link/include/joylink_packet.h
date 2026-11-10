// AERO RC LINK -- the JoyLink wire format, built on the board.
//
// THIS IS NOT A NEW PROTOCOL. The owner rejected a custom BLE protocol, and rightly; this is the protocol the game
// ALREADY SHIPS, written down from the two files that implement it so the board can stand in for the Mac:
//
//   unity/Assets/_Project/Scripts/Bridge/JoyLink.cs   -- builds and reads the packets (the sender is the Mac)
//   unity/Assets/Plugins/iOS/JoyLink.mm               -- the transport: Bonjour `_aerojoy._udp` + UDP
//
// and the iOS side needs NOTHING added, because it is all there already:
//   * JoyLink.mm's receiver browses for `_aerojoy._udp`, connects to the first endpoint it finds, says "AJ1H"
//     twice a second, and keeps the latest packet;
//   * BuildScript.cs line 442 already declares `_aerojoy._udp` in NSBonjourServices, which iOS requires before an
//     app may browse at all, and line 441 already supplies NSLocalNetworkUsageDescription;
//   * JoystickInput reads the channels through a calibrated JoyAxis per control and a profile per device NAME.
// The board just has to be a device on that service that sends the same bytes. Nothing in unity/ changes.
//
// THE PACKETS (byte for byte, from JoyLink.cs Send() and the length check in Receive()):
//
//   "AJ1D"  channels, up to 100 Hz
//     [0..3]   'A' 'J' '1' 'D'
//     [4..5]   sequence, uint16 little-endian (wraps; the receiver does not check it, but the Mac sends it)
//     [6..37]  8 x float32 little-endian -- the RAW axes, -1..1. Eight exactly: JoystickInput.AxisCount is 8 and
//              JoyLink.mm's receiver requires `len >= 4 + 2 + 32 + 4 + 1`.
//     [38..41] buttons, uint32 little-endian, bit b = button b (JoystickInput.ButtonCount is 32)
//     [42]     name length, uint8 (<= 120)
//     [43..]   the device name, UTF-8. This is what becomes JoystickInput.ConnectedName, which is the KEY the
//              calibration profile is stored under (PlayerPrefs "joy.profile.<name>"), so it must never change
//              between boots or between formats -- a board that called itself "Aero RC Link (SBUS)" would lose
//              his calibration the day he flew the PPM radio. One constant name, always.
//
//   "AJ1P"  the calibration profile, 1 Hz
//     [0..3]   'A' 'J' '1' 'P'
//     [4..]    JoystickProfile as JSON (Newtonsoft's own shape -- the C# class is in JoystickSetup.cs)
//     The receiver adopts it ONLY when this device has no profile of its own for that name:
//     `if (p?.Name != null && JoystickInput.Load(p.Name) == null) JoystickInput.Save(p);`
//     So this is a first-flight default, not an override: the moment he runs OPTIONS > JOYSTICK SETUP, his own
//     calibration wins forever. That is what makes the first power-up fly without a setup step.
//
//   "AJ1H"  hello, from the iPad, twice a second. The Mac only sends to peers it has heard from in the last 3 s,
//     and the board does the same -- it is what keeps the board off the air when nothing is listening, and it is
//     how the board learns the iPad's address and port without any discovery of its own.
//
// Endianness: the packet is little-endian and so are the ESP32, the iPhone, the iPad and the Mac. A plain memcpy
// of a float is therefore the format, which is what JoyLink.cs does with BitConverter.TryWriteBytes.

#pragma once

#include <cstdint>
#include <cstddef>
#include <cstdio>
#include <cstring>

namespace rclink {

/// The board's device name on the wire. One constant, for the reason in the header comment.
inline constexpr const char* kDeviceName = "Aero RC Link";

/// The Bonjour service the iOS app browses for, and the UDP port the board publishes in its SRV record. The port
/// number itself is arbitrary -- the app takes it from Bonjour, never from a constant -- but a fixed one makes the
/// board easy to prod with `nc` while debugging.
inline constexpr const char* kBonjourService = "aerojoy";   // "_aerojoy._udp"
inline constexpr uint16_t    kUdpPort = 48711;

inline constexpr size_t kAJ1DLen = 4 + 2 + 32 + 4 + 1;   // + the name bytes
inline constexpr size_t kMaxPacket = 1500;               // JoyLink.mm's receive buffer

inline void putU16(uint8_t* p, uint16_t v) { p[0] = uint8_t(v & 0xFF); p[1] = uint8_t(v >> 8); }
inline void putU32(uint8_t* p, uint32_t v)
{
    p[0] = uint8_t(v & 0xFF); p[1] = uint8_t((v >> 8) & 0xFF);
    p[2] = uint8_t((v >> 16) & 0xFF); p[3] = uint8_t((v >> 24) & 0xFF);
}
inline void putF32(uint8_t* p, float v) { std::memcpy(p, &v, 4); }

/// Build an "AJ1D" channel packet. Returns its length, or 0 if the buffer is too small.
inline size_t buildChannelPacket(uint8_t* buf, size_t cap, uint16_t seq,
                                 const float* axes8, uint32_t buttons, const char* name)
{
    size_t nameLen = 0;
    if (name) { while (name[nameLen] && nameLen < 120) nameLen++; }
    size_t need = kAJ1DLen + nameLen;
    if (cap < need) return 0;

    buf[0] = 'A'; buf[1] = 'J'; buf[2] = '1'; buf[3] = 'D';
    putU16(buf + 4, seq);
    for (int i = 0; i < 8; i++) putF32(buf + 6 + i * 4, axes8[i]);
    putU32(buf + 38, buttons);
    buf[42] = uint8_t(nameLen);
    if (nameLen) std::memcpy(buf + 43, name, nameLen);
    return need;
}

/// Is this an "AJ1H" hello from an iPad or iPhone? (That is the only packet the board ever receives.)
inline bool isHello(const uint8_t* buf, size_t len)
{
    return len >= 4 && buf[0] == 'A' && buf[1] == 'J' && buf[2] == '1' && buf[3] == 'H';
}

// ---------------------------------------------------------------------------------------------------------------
// The first-flight profile
// ---------------------------------------------------------------------------------------------------------------
// Written out by hand rather than with a JSON library, because it is one fixed shape and a library is 40 kB of
// flash to print nine objects. The field names and the SEMANTICS are taken from JoystickSetup.cs:
//
//   JoyAxis { Index (1..8), Center, PosEnd, NegEnd, Dead, Centering }
//     Value()   centring axis, -1..1, + = RIGHT / PUSH (nose down) / RIGHT RUDDER, dead zone taken out
//     Value01() a throttle (Centering = false): NegEnd is IDLE, PosEnd is FULL
//   FlapSwitch is a JoyAxis with Centering = false, read three ways: < 0.25 up, < 0.75 half, else full
//   Buttons    action -> button index (0-based)
//
// The board sends its channels against a fixed 1000/1500/2000 reference, so the profile's endpoints are the
// nominal +-1 and the real travel is picked up later by the wizard. `Dead` is 0.03, the wizard's own default:
// enough to kill a trainer-port wire's noise on a centred stick, small enough that the owner will not feel it
// (the game's Aircraft.ShapeAxis zones it again downstream anyway).
//
// SENSES: if an axis flies backwards, reverse that channel ON THE TRANSMITTER. See the note in rc_map.h.

/// Append a JoyAxis object. `centering` false makes it a throttle-style axis (NegEnd = idle).
inline int writeAxis(char* out, size_t cap, const char* field, uint8_t index, bool centering)
{
    const char* c = centering ? "true" : "false";
    int n = std::snprintf(out, cap,
        "\"%s\":{\"Index\":%u,\"Center\":%s,\"PosEnd\":1.0,\"NegEnd\":-1.0,\"Dead\":0.03,\"Centering\":%s}",
        field, unsigned(index), centering ? "0.0" : "-1.0", c);
    return (n < 0 || size_t(n) >= cap) ? -1 : n;
}

/// Build the "AJ1P" profile packet. `ail`/`ele`/`thr`/`rud` are 1-based channel numbers (FunctionChannels).
/// `flapChannel` is the 1-based channel of the flap switch, or 0 for none.
/// Returns the packet length, or 0 if it did not fit.
inline size_t buildProfilePacket(uint8_t* buf, size_t cap,
                                 uint8_t ail, uint8_t ele, uint8_t thr, uint8_t rud, uint8_t flapChannel)
{
    if (cap < 64) return 0;
    char* j = reinterpret_cast<char*>(buf) + 4;
    size_t room = cap - 4;
    int n = 0, k;

    k = std::snprintf(j + n, room - n, "{\"Name\":\"%s\",", kDeviceName);
    if (k < 0 || size_t(n + k) >= room) return 0;
    n += k;

    struct { const char* field; uint8_t ch; bool centering; } axes[] = {
        { "Aileron",  ail, true  },
        { "Elevator", ele, true  },
        { "Rudder",   rud, true  },
        { "Throttle", thr, false },
    };
    for (const auto& a : axes)
    {
        k = writeAxis(j + n, room - n, a.field, a.ch, a.centering);
        if (k < 0) return 0;
        n += k;
        k = std::snprintf(j + n, room - n, ",");
        if (k < 0 || size_t(n + k) >= room) return 0;
        n += k;
    }

    if (flapChannel)
    {
        k = writeAxis(j + n, room - n, "FlapSwitch", flapChannel, false);
        if (k < 0) return 0;
        n += k;
        k = std::snprintf(j + n, room - n, ",");
        if (k < 0 || size_t(n + k) >= room) return 0;
        n += k;
    }

    // The three actions worth having before he has run the wizard, on the switches above the flap switch.
    // Button b comes from channel b + 1 (rc_map.h: buttons 4..15 <- channels 5..16), so 5/6/7 are channels 6/7/8.
    // GEAR is a `Pressed` action, so a two-position switch toggles it on each flip, which is what a gear switch
    // should do. GUNS and BRAKES are `Held`, so a switch holds them, which is also right.
    k = std::snprintf(j + n, room - n,
        "\"Buttons\":{\"gear\":5,\"guns\":6,\"smoke\":7},\"AxisButtons\":{}}");
    if (k < 0 || size_t(n + k) >= room) return 0;
    n += k;

    buf[0] = 'A'; buf[1] = 'J'; buf[2] = '1'; buf[3] = 'P';
    return size_t(4 + n);
}

}   // namespace rclink

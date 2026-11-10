// AERO RC LINK -- host tests. These run on the Mac with clang and no hardware: `tools/rc-link/test/run.sh`.
//
// WHY THESE EXIST AT ALL: nobody has the board. Everything in src/main.cpp that touches a pin, a radio or a
// Bluetooth stack is UNTESTED and will stay untested until Ty flashes it. So the firmware was deliberately split:
// every decision with a right and a wrong answer lives in include/*.h as pure C++ with no Arduino in it, and this
// file runs all of it. What is left in main.cpp is wiring, and wiring fails loudly on the first power-up.
//
// The sources of truth these tests are checked against:
//   PPM/SBUS       the formats themselves (the numbers are in the comments in rc_decode.h)
//   the game       src/FlyingGame.Core/GamepadShaping.cs, re-implemented below as `GameLever` so the throttle
//                  servo is tested against the actual law it is trying to drive, not against my memory of it
//   the packet     unity/Assets/_Project/Scripts/Bridge/JoyLink.cs and Assets/Plugins/iOS/JoyLink.mm

#include "../include/rc_decode.h"
#include "../include/rc_map.h"
#include "../include/joylink_packet.h"

#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

using namespace rclink;

static int gFail = 0, gRun = 0;

#define CHECK(cond, ...) do { gRun++; if (!(cond)) { gFail++; \
    std::printf("  FAIL %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)

static bool near(float a, float b, float tol = 1e-4f) { return std::fabs(a - b) <= tol; }

// ---------------------------------------------------------------------------------------------------------------
// PPM
// ---------------------------------------------------------------------------------------------------------------

// Feed one PPM frame as a mark train. `markUs` is the pulse width, which the decoder must be blind to.
static void feedPpmFrame(PpmDecoder& d, uint32_t& t, const std::vector<uint16_t>& chans,
                         uint32_t syncUs = 6000, uint32_t markUs = 300, bool inverted = false)
{
    // A positive frame is mark, gap, mark, gap... and the RISING edge starts each mark. An inverted frame is the
    // same train upside down, so the FALLING edge starts each mark -- but the rising edges are then every mark
    // start plus one constant mark width, which leaves every interval identical. That is the point of the test:
    // we feed the rising edges of the inverted waveform and expect the same answer.
    for (uint16_t c : chans)
    {
        d.feedEdge(inverted ? t + markUs : t);
        t += c;
    }
    d.feedEdge(inverted ? t + markUs : t);   // the edge that starts the sync gap
    t += syncUs;
}

static void testPpm()
{
    std::printf("PPM\n");
    {
        PpmDecoder d;
        uint32_t t = 100000;
        std::vector<uint16_t> ch = { 1500, 1100, 1000, 1900, 1750, 1500, 2000, 1250 };
        feedPpmFrame(d, t, ch);       // the first frame has no leading sync, so it is dropped
        feedPpmFrame(d, t, ch);
        CHECK(d.frames() >= 1, "no frame decoded (frames=%u)", d.frames());
        CHECK(d.frame().count == 8, "count %u, want 8", d.frame().count);
        for (size_t i = 0; i < ch.size(); i++)
            CHECK(d.frame().us[i] == ch[i], "ch%zu = %u, want %u", i + 1, d.frame().us[i], ch[i]);
    }
    {
        // InvertedPpmDecodesTheSame -- the claim in rc_decode.h's header comment, which is why there is no
        // polarity setting anywhere in this firmware.
        std::vector<uint16_t> ch = { 1500, 1100, 1000, 1900, 1750, 1500 };
        PpmDecoder a, b;
        uint32_t ta = 50000, tb = 50000;
        for (int k = 0; k < 3; k++) { feedPpmFrame(a, ta, ch, 6000, 300, false); feedPpmFrame(b, tb, ch, 6000, 300, true); }
        CHECK(a.frame().count == b.frame().count, "counts differ: %u vs %u", a.frame().count, b.frame().count);
        for (uint8_t i = 0; i < a.frame().count; i++)
            CHECK(a.frame().us[i] == b.frame().us[i], "ch%u differs: %u vs %u", i + 1, a.frame().us[i], b.frame().us[i]);
    }
    {
        // A glitch poisons its frame and the frame is DROPPED, not published short. Half a frame of garbage
        // reaching the flight model is the thing we are protecting against.
        PpmDecoder d;
        uint32_t t = 10000;
        std::vector<uint16_t> ch = { 1500, 1500, 1500, 1500, 1500, 1500 };
        feedPpmFrame(d, t, ch);
        feedPpmFrame(d, t, ch);
        // A frame is published by the edge that OPENS the next one, so the second frame is still pending here.
        // This edge flushes it and starts a fresh frame; only then is the baseline meaningful.
        d.feedEdge(t);
        uint32_t good = d.frames();
        // Now a frame with a 50 us runt interval in the middle of it.
        t += 1500;
        d.feedEdge(t); t += 50;
        d.feedEdge(t); t += 1500;
        d.feedEdge(t); t += 1500;
        d.feedEdge(t); t += 1500;
        d.feedEdge(t); t += 6000;
        d.feedEdge(t); t += 6000;   // (leave t one sync gap ahead, the way feedPpmFrame leaves it)
        CHECK(d.frames() == good, "a poisoned frame was published (%u -> %u)", good, d.frames());
        // ...and the next clean frame comes through, so one glitch is not a lockout.
        feedPpmFrame(d, t, ch);
        feedPpmFrame(d, t, ch);   // the second frame's opening edge is what publishes the first
        CHECK(d.frames() == good + 1, "the decoder did not recover (%u)", d.frames());
    }
    {
        // Too few channels is not a transmitter.
        PpmDecoder d;
        uint32_t t = 1000;
        std::vector<uint16_t> ch = { 1500, 1500 };
        for (int k = 0; k < 4; k++) feedPpmFrame(d, t, ch);
        CHECK(d.frames() == 0, "a 2-channel 'frame' was accepted");
    }
    {
        // micros() wraps every ~71 minutes. Unsigned arithmetic must carry straight across it.
        PpmDecoder d;
        uint32_t t = 0xFFFFF000;
        std::vector<uint16_t> ch = { 1500, 1100, 1000, 1900, 1750, 1500 };
        feedPpmFrame(d, t, ch);
        feedPpmFrame(d, t, ch);   // this frame straddles the wrap
        feedPpmFrame(d, t, ch);
        CHECK(d.frames() >= 2, "frames lost across the micros() wrap (%u)", d.frames());
        CHECK(d.frame().us[3] == 1900, "ch4 = %u across the wrap, want 1900", d.frame().us[3]);
    }
}

// ---------------------------------------------------------------------------------------------------------------
// SBUS
// ---------------------------------------------------------------------------------------------------------------

static std::vector<uint8_t> sbusFrame(const uint16_t counts[16], uint8_t flags = 0, uint8_t endByte = 0x00)
{
    std::vector<uint8_t> f(25, 0);
    f[0] = 0x0F;
    uint32_t bit = 0;
    for (int c = 0; c < 16; c++)
        for (int k = 0; k < 11; k++, bit++)
            if ((counts[c] >> k) & 1) f[1 + (bit >> 3)] |= uint8_t(1u << (bit & 7));
    f[23] = flags;
    f[24] = endByte;
    return f;
}

static void testSbus()
{
    std::printf("SBUS\n");
    {
        // The standard conversion: 172 = -100%, 992 = centre, 1811 = +100%, integer `1500 + (c - 992) * 5 / 8`.
        CHECK(sbusToUs(992) == 1500, "centre = %u, want 1500", sbusToUs(992));
        CHECK(sbusToUs(172) == 988, "-100%% = %u, want 988", sbusToUs(172));
        CHECK(sbusToUs(1811) == 2011, "+100%% = %u, want 2011", sbusToUs(1811));
        // Both endpoints must land inside the band the rest of the firmware accepts, or a stick at the stop would
        // be rejected as noise on every frame.
        CHECK(sbusToUs(172) >= kChMinUs && sbusToUs(1811) <= kChMaxUs, "SBUS endpoints fall outside kChMin/MaxUs");
    }
    {
        // Channel 9 and up: the bit cursor against a hand-checked frame. This is where SBUS parsers go wrong,
        // which is why rc_decode.h uses a cursor instead of unrolled shifts.
        uint16_t counts[16];
        for (int c = 0; c < 16; c++) counts[c] = uint16_t(200 + c * 100);   // 200, 300, ... 1700: all distinct
        auto f = sbusFrame(counts);
        SbusDecoder d;
        bool got = false;
        for (uint8_t b : f) got = d.feedByte(b) || got;
        CHECK(got, "no SBUS frame decoded");
        CHECK(d.frame().count == 18, "count %u, want 18 (16 + ch17/18)", d.frame().count);
        for (int c = 0; c < 16; c++)
            CHECK(d.frame().us[c] == sbusToUs(counts[c]), "ch%d = %u, want %u", c + 1,
                  d.frame().us[c], sbusToUs(counts[c]));
    }
    {
        // The flags in byte 23 -- the half of the failsafe story that PPM cannot tell.
        uint16_t counts[16] = {};
        for (auto& c : counts) c = 992;
        SbusDecoder d;
        for (uint8_t b : sbusFrame(counts, 0x00)) d.feedByte(b);
        CHECK(!d.frame().failsafe && !d.frame().frameLost, "clean frame reported flags");
        for (uint8_t b : sbusFrame(counts, 0x04)) d.feedByte(b);
        CHECK(d.frame().frameLost && !d.frame().failsafe, "bit 2 is FRAME LOST");
        for (uint8_t b : sbusFrame(counts, 0x08)) d.feedByte(b);
        CHECK(d.frame().failsafe && !d.frame().frameLost, "bit 3 is FAILSAFE");
        for (uint8_t b : sbusFrame(counts, 0x03)) d.feedByte(b);
        CHECK(d.frame().us[16] == 2000 && d.frame().us[17] == 2000, "ch17/ch18 are bits 0 and 1");
    }
    {
        // Futaba's counted end byte (low nibble 0 or 4) is accepted; anything else is a false lock and is dropped.
        uint16_t counts[16] = {};
        for (auto& c : counts) c = 992;
        for (uint8_t end : { 0x00, 0x04, 0x14, 0x24, 0x34 })
        {
            SbusDecoder d;
            bool got = false;
            for (uint8_t b : sbusFrame(counts, 0, uint8_t(end))) got = d.feedByte(b) || got;
            CHECK(got, "end byte 0x%02X rejected", end);
        }
        SbusDecoder d;
        bool got = false;
        for (uint8_t b : sbusFrame(counts, 0, 0x55)) got = d.feedByte(b) || got;
        CHECK(!got, "end byte 0x55 accepted");
    }
    {
        // Mid-stream pickup: start halfway through a frame and the decoder must resynchronise, not publish junk.
        uint16_t counts[16];
        for (int c = 0; c < 16; c++) counts[c] = uint16_t(300 + c * 90);
        auto f = sbusFrame(counts);
        SbusDecoder d;
        for (size_t i = 11; i < f.size(); i++) d.feedByte(f[i]);   // the tail of a frame
        for (int k = 0; k < 3; k++) for (uint8_t b : f) d.feedByte(b);
        CHECK(d.frames() >= 2, "did not resynchronise mid-stream (%u frames)", d.frames());
        for (int c = 0; c < 16; c++)
            CHECK(d.frame().us[c] == sbusToUs(counts[c]), "after resync ch%d = %u", c + 1, d.frame().us[c]);
    }
}

// ---------------------------------------------------------------------------------------------------------------
// Format detection
// ---------------------------------------------------------------------------------------------------------------

static void testDetect()
{
    std::printf("format detection\n");
    {
        // SBUS is listened to first (the reason is in rc_decode.h) and locks once four frames land.
        FormatDetector d;
        d.begin(0);
        CHECK(d.listening() == Format::Sbus, "the first phase should be SBUS");
        d.update(10, 0, 4);
        CHECK(d.format() == Format::Sbus, "did not lock SBUS on 4 frames");
    }
    {
        // Nothing on SBUS: the phase flips to PPM after kPhaseMs and locks there.
        FormatDetector d;
        d.begin(0);
        d.update(100, 0, 0);
        CHECK(d.format() == Format::None, "locked with no frames");
        bool changed = d.update(FormatDetector::kPhaseMs + 1, 0, 0);
        CHECK(changed && d.listening() == Format::Ppm, "did not move to the PPM phase");
        d.update(FormatDetector::kPhaseMs + 50, 4, 0);
        CHECK(d.format() == Format::Ppm, "did not lock PPM");
    }
    {
        // Three frames is not a lock: one stray 0x0F in a PPM stream must not win.
        FormatDetector d;
        d.begin(0);
        d.update(10, 0, 3);
        CHECK(d.format() == Format::None, "locked on 3 frames");
    }
    {
        // A locked format with no frames for kLostMs unlocks and hunts again -- changing radios needs no reboot.
        FormatDetector d;
        d.begin(0);
        d.update(10, 0, 4);
        CHECK(d.format() == Format::Sbus, "setup");
        for (uint32_t t = 20; t < 20 + FormatDetector::kLostMs; t += 10) d.update(t, 0, 4);
        CHECK(d.format() == Format::Sbus, "unlocked too early");
        d.update(20 + FormatDetector::kLostMs + 10, 0, 4);
        CHECK(d.format() == Format::None, "did not unlock after kLostMs of silence");
    }
}

// ---------------------------------------------------------------------------------------------------------------
// Calibration
// ---------------------------------------------------------------------------------------------------------------

static void testCal()
{
    std::printf("calibration\n");
    {
        Cal k;   // the defaults, which is what the JoyLink path always uses
        CHECK(near(k.bipolar(1500), 0.f), "centre is not 0");
        CHECK(near(k.bipolar(2000), 1.f), "full is not 1");
        CHECK(near(k.bipolar(1000), -1.f), "idle is not -1");
        CHECK(near(k.bipolar(1750), 0.5f), "half is not 0.5");
        CHECK(near(k.unipolar(1000), 0.f) && near(k.unipolar(2000), 1.f), "unipolar ends");
        CHECK(near(k.bipolar(2500), 1.f) && near(k.bipolar(500), -1.f), "out of range is not clamped");
    }
    {
        // The two halves scale independently, which is the point: a transmitter's centre is not the midpoint of
        // its travel, and one scale factor would make full left and full right different sizes.
        Cal k{ 1050, 1480, 1960 };
        CHECK(near(k.bipolar(1480), 0.f), "sub-trimmed centre");
        CHECK(near(k.bipolar(1960), 1.f), "high end");
        CHECK(near(k.bipolar(1050), -1.f), "low end");
        CHECK(near(k.bipolar(1720), 0.5f, 2e-3f), "high half midpoint = %f", k.bipolar(1720));
        CHECK(near(k.bipolar(1265), -0.5f, 2e-3f), "low half midpoint = %f", k.bipolar(1265));
    }
    {
        // A sweep: stir everything, then release. The channel that was never moved keeps its default.
        CalSweep s;
        s.begin();
        Frame f;
        f.count = 8;
        for (int i = 0; i < 8; i++) f.us[i] = 1500;
        // Channels 1..4 swept to real, asymmetric endpoints; 5..8 left alone.
        const uint16_t lo[4] = { 1020, 1080, 990, 1040 }, hi[4] = { 1980, 1920, 2010, 1940 };
        for (int step = 0; step <= 10; step++)
        {
            for (int c = 0; c < 4; c++)
                f.us[c] = uint16_t(lo[c] + (hi[c] - lo[c]) * step / 10);
            s.feed(f);
        }
        Frame rest;
        rest.count = 8;
        for (int i = 0; i < 8; i++) rest.us[i] = 1500;
        rest.us[0] = 1496; rest.us[1] = 1512;   // real sticks do not rest at exactly 1500
        rest.us[2] = 1000;                      // the throttle rests at idle, which IS its centre for Value01
        rest.us[3] = 1503;

        Cal cal[kMaxChannels];
        uint8_t good = s.finish(rest, cal);
        CHECK(good == 4, "%u channels calibrated, want 4", good);
        for (int c = 0; c < 4; c++)
        {
            CHECK(cal[c].lo == lo[c], "ch%d lo = %u, want %u", c + 1, cal[c].lo, lo[c]);
            CHECK(cal[c].hi == hi[c], "ch%d hi = %u, want %u", c + 1, cal[c].hi, hi[c]);
        }
        CHECK(cal[2].mid == 1000 && cal[2].mid >= cal[2].lo, "the throttle's rest position must be clamped inside its travel");
        CHECK(cal[5].lo == 1000 && cal[5].hi == 2000, "an untouched channel must keep its default, not a 1-point span");
        CHECK(near(cal[0].bipolar(1496), 0.f), "a calibrated channel reads 0 at its measured rest");
    }
    {
        Cal bad{ 1500, 1500, 1500 };
        CHECK(!bad.plausible(), "a stick that never moved must not be plausible");
        Cal barely{ 1450, 1500, 1550 };
        CHECK(!barely.plausible(), "100 us of travel is noise, not a control");
        // A THROTTLE rests at the end of its travel. This must be plausible, and the earlier version of
        // plausible() -- which demanded a centre 100 us inside both ends -- rejected it and silently threw away
        // the calibration of the one channel the owner notices most.
        Cal throttle{ 1000, 1000, 2000 };
        CHECK(throttle.plausible(), "a throttle resting at idle must be plausible");
        Cal outOfBand{ 400, 1500, 2600 };
        CHECK(!outOfBand.plausible(), "endpoints outside kChMinUs..kChMaxUs are a decode fault, not a travel");
    }
}

// ---------------------------------------------------------------------------------------------------------------
// Failsafe -- the decision in rc_map.h, exercised
// ---------------------------------------------------------------------------------------------------------------

static void testFailsafe()
{
    std::printf("failsafe\n");
    Frame good;
    good.count = 8;
    for (int i = 0; i < 8; i++) good.us[i] = 1500;

    {
        Failsafe fs;
        fs.begin(1000);
        fs.onFrame(1000, good);
        CHECK(fs.update(1000) == Link::Live, "a fresh frame is LIVE");

        // Fly it: full aileron, throttle at 70%.
        float a = 1.f, e = -0.4f, r = 0.3f, t = 0.7f;
        fs.apply(a, e, r, t);
        CHECK(near(a, 1.f) && near(t, 0.7f), "LIVE must pass the controls through untouched");

        // The lead falls out. 100 ms later: surfaces centred, throttle HELD. This is the decision.
        CHECK(fs.update(1100) == Link::Lost, "100 ms with no frame is LOST");
        a = 1.f; e = -0.4f; r = 0.3f; t = 0.7f;
        fs.apply(a, e, r, t);
        CHECK(near(a, 0.f) && near(e, 0.f) && near(r, 0.f), "LOST must centre the surfaces");
        CHECK(near(t, 0.7f), "LOST must HOLD the throttle, got %f", t);

        // Still nothing at two seconds: he is not flying it. Throttle to idle.
        CHECK(fs.update(3001) == Link::Dead, "2 s with no frame is DEAD");
        a = 1.f; t = 0.7f;
        fs.apply(a, e, r, t);
        CHECK(near(t, 0.f), "DEAD must close the throttle, got %f", t);

        // It comes back. Everything passes through again on the first good frame.
        fs.onFrame(3100, good);
        CHECK(fs.update(3100) == Link::Live, "a frame after DEAD must restore LIVE");
    }
    {
        // SBUS tells the truth faster than the clock can: a receiver in failsafe keeps sending at full rate with
        // the bit set, so a time-only failsafe would NEVER fire. This is the test that catches that mistake.
        Failsafe fs;
        fs.begin(0);
        fs.onFrame(0, good);
        Frame fsFrame = good;
        fsFrame.failsafe = true;
        for (uint32_t t = 10; t <= 500; t += 7) { fs.onFrame(t, fsFrame); fs.update(t); }
        CHECK(fs.state() != Link::Live, "a stream of failsafe-flagged frames was treated as LIVE");

        Failsafe fl;
        fl.begin(0);
        fl.onFrame(0, good);
        Frame lost = good;
        lost.frameLost = true;
        for (uint32_t t = 10; t <= 500; t += 7) { fl.onFrame(t, lost); fl.update(t); }
        CHECK(fl.state() != Link::Live, "a stream of frame-lost frames was treated as LIVE");
    }
    {
        // Never flown yet, then the link drops: idle, not a guess at the last throttle.
        Failsafe fs;
        fs.begin(0);
        CHECK(fs.update(5000) == Link::Dead, "no frame ever = DEAD");
        float a = 0, e = 0, r = 0, t = 0.9f;
        fs.apply(a, e, r, t);
        CHECK(near(t, 0.f), "with no throttle ever seen the output must be idle");
    }
}

// ---------------------------------------------------------------------------------------------------------------
// The two output maps
// ---------------------------------------------------------------------------------------------------------------

static void testMaps()
{
    std::printf("mapping\n");
    Cal cal[kMaxChannels];   // defaults

    {
        CHECK(FunctionChannels::of(Order::AETR).elevator == 2, "AETR elevator is ch2");
        CHECK(FunctionChannels::of(Order::TAER).throttle == 1, "TAER throttle is ch1");
        CHECK(FunctionChannels::of(Order::TAER).elevator == 3, "TAER elevator is ch3");
        CHECK(FunctionChannels::of(Order::RETA).aileron == 4, "RETA aileron is ch4");
    }
    {
        Frame f;
        f.count = 10;
        for (int i = 0; i < 10; i++) f.us[i] = 1500;
        f.us[0] = 2000;   // full right aileron
        f.us[2] = 1000;   // throttle idle
        f.us[5] = 2000;   // channel 6: a switch ON
        f.us[8] = 2000;   // channel 9: a switch ON (SBUS territory -- no axis, but a button)

        JoyLinkOut o = JoyLinkOut::from(f, cal);
        CHECK(near(o.axes[0], 1.f), "axis 1 should be full right");
        CHECK(near(o.axes[2], -1.f), "axis 3 should be idle");
        // Eight axes exactly: JoystickInput.AxisCount. Channels 9 and 10 have nowhere analogue to go.
        CHECK(near(o.axes[7], 0.f), "axis 8 is the last one");

        // Buttons 4..15 <- channels 5..16, and buttons 0..3 are never set (the wizard-assignment trap).
        CHECK((o.buttons & 0x0F) == 0, "buttons 0..3 must never be set, got 0x%08X", o.buttons);
        CHECK((o.buttons & (1u << 5)) != 0, "channel 6 should be button 5");
        CHECK((o.buttons & (1u << 8)) != 0, "channel 9 should be button 8 -- SBUS's extra channels as switches");
        CHECK((o.buttons & (1u << 4)) == 0, "a centred channel 5 is not a pressed button");

        // A packet that the shipping receiver would accept.
        uint8_t buf[kMaxPacket];
        size_t n = buildChannelPacket(buf, sizeof buf, 7, o.axes, o.buttons, kDeviceName);
        CHECK(n == kAJ1DLen + std::strlen(kDeviceName), "packet length %zu", n);
        CHECK(n >= 4 + 2 + 32 + 4 + 1, "JoyLink.mm's own length check");
        CHECK(buf[0] == 'A' && buf[1] == 'J' && buf[2] == '1' && buf[3] == 'D', "magic");
        CHECK(buf[4] == 7 && buf[5] == 0, "sequence is little-endian uint16");
        float back;
        std::memcpy(&back, buf + 6, 4);
        CHECK(near(back, 1.f), "axis 1 did not survive the round trip (%f)", back);
        uint32_t btn = uint32_t(buf[38]) | (uint32_t(buf[39]) << 8) | (uint32_t(buf[40]) << 16) | (uint32_t(buf[41]) << 24);
        CHECK(btn == o.buttons, "buttons did not survive the round trip");
        CHECK(buf[42] == std::strlen(kDeviceName), "name length byte");
        CHECK(std::memcmp(buf + 43, kDeviceName, std::strlen(kDeviceName)) == 0, "name bytes");
        CHECK(buildChannelPacket(buf, 20, 0, o.axes, 0, kDeviceName) == 0, "a short buffer must be refused");
    }
    {
        // The profile packet: "AJ1P" + JSON that Newtonsoft can read into JoystickProfile. The JSON is not parsed
        // here (there is no parser on the board either) -- what is checked is that every field the C# class needs
        // is present and spelled the way JoystickSetup.cs spells it.
        uint8_t buf[kMaxPacket];
        FunctionChannels fc = FunctionChannels::of(Order::AETR);
        size_t n = buildProfilePacket(buf, sizeof buf, fc.aileron, fc.elevator, fc.throttle, fc.rudder, 5);
        CHECK(n > 0, "profile packet did not build");
        CHECK(buf[3] == 'P', "JoyLink.mm routes on byte 3 == 'P'");
        std::string j(reinterpret_cast<char*>(buf) + 4, n - 4);
        for (const char* field : { "\"Name\":\"Aero RC Link\"", "\"Aileron\"", "\"Elevator\"", "\"Rudder\"",
                                   "\"Throttle\"", "\"FlapSwitch\"", "\"Buttons\"", "\"AxisButtons\"",
                                   "\"Index\"", "\"Center\"", "\"PosEnd\"", "\"NegEnd\"", "\"Dead\"", "\"Centering\"" })
            CHECK(j.find(field) != std::string::npos, "the profile JSON is missing %s", field);
        CHECK(j.find("\"Throttle\":{\"Index\":3,\"Center\":-1.0,\"PosEnd\":1.0,\"NegEnd\":-1.0,\"Dead\":0.03,\"Centering\":false}")
                  != std::string::npos, "the throttle must be Centering:false with NegEnd = idle: %s", j.c_str());
        CHECK(j.find("\"Centering\":true") != std::string::npos, "the stick axes must be centring");
        CHECK(j.front() == '{' && j.back() == '}', "the JSON must be a single object: %s", j.c_str());
        // Brace and quote balance -- a trailing comma or an unclosed object is the way a hand-written JSON
        // generator fails, and the failure would be silent (the iPad would just never adopt the profile).
        int depth = 0;
        size_t quotes = 0;
        for (char c : j) { if (c == '{') depth++; else if (c == '}') depth--; else if (c == '"') quotes++; }
        CHECK(depth == 0, "unbalanced braces in the profile JSON");
        CHECK(quotes % 2 == 0, "unbalanced quotes in the profile JSON");
        CHECK(j.find(",}") == std::string::npos && j.find(",,") == std::string::npos, "trailing comma: %s", j.c_str());
        CHECK(buildProfilePacket(buf, 40, 1, 2, 3, 4, 5) == 0, "a short buffer must be refused");
    }
    {
        CHECK(isHello(reinterpret_cast<const uint8_t*>("AJ1H"), 4), "the iPad's hello");
        CHECK(!isHello(reinterpret_cast<const uint8_t*>("AJ1D"), 4), "a channel packet is not a hello");
        CHECK(!isHello(reinterpret_cast<const uint8_t*>("AJ"), 2), "a runt is not a hello");
    }
    {
        // The gamepad map. The triggers stay at zero on purpose: GamepadShaping.Rudder adds (RT - LT) to the
        // rudder, so anything parked on a trigger is a standing rudder deflection.
        Frame f;
        f.count = 8;
        for (int i = 0; i < 8; i++) f.us[i] = 1500;
        f.us[0] = 2000;   // aileron right
        f.us[1] = 1000;   // elevator
        f.us[3] = 1750;   // rudder half right
        GamepadOut g = GamepadOut::from(f, cal, FunctionChannels::of(Order::AETR), 0.f);
        CHECK(g.rx > 32000, "roll on the RIGHT stick X (%d)", g.rx);
        CHECK(g.ry < -32000, "pitch on the RIGHT stick Y (%d)", g.ry);
        CHECK(g.lx > 15000 && g.lx < 18000, "rudder on the LEFT stick X (%d)", g.lx);
        CHECK(g.lt == 0 && g.rt == 0, "the triggers must stay at zero -- they are a second rudder");
        CHECK(g.ly == 0, "the throttle axis comes from ThrottleServo, not from the channel");
    }
}

// ---------------------------------------------------------------------------------------------------------------
// The throttle servo, against the GAME'S OWN LAW
// ---------------------------------------------------------------------------------------------------------------
// `GameLever` below is GamepadShaping.DeadZone + GamepadShaping.Lever, transcribed from
// src/FlyingGame.Core/GamepadShaping.cs. Testing the servo against anything else would be testing it against my
// memory of the game, which is the mistake this is here to avoid.

struct GameLever
{
    double lever = 0.0;

    static double deadZone(double x, double dz)
    {
        double c = x > 1 ? 1 : x < -1 ? -1 : x, m = std::fabs(c);
        if (m <= dz) return 0.0;
        double s = c < 0 ? -1.0 : 1.0, v = (m - dz) / (1.0 - dz);
        return s * (v > 1 ? 1 : v);
    }

    /// GamepadShaping.Lever with idle 0, full 1: ratePerSec = |full - idle| / ThrottleIdleToFullS.
    void step(double stickY, double dt)
    {
        double rate = deadZone(stickY, ThrottleServo::kDeadZone) * (1.0 / ThrottleServo::kIdleToFullS);
        if (rate == 0.0) return;             // nothing asking for a change leaves the lever exactly where it was
        lever += rate * dt;
        if (lever < 0) lever = 0;
        if (lever > 1) lever = 1;
    }
};

static void testThrottleServo()
{
    std::printf("throttle servo\n");
    {
        // The naive map -- RC throttle straight onto leftStick.y -- is a four-position throttle. This test records
        // WHY the servo exists: an absolute stick at 60%% (y = +0.2) sits inside the 0.25 dead zone and the lever
        // never moves at all; at 80%% (y = +0.6) it ramps to FULL and stays there.
        GameLever naive60, naive80;
        for (int i = 0; i < 600; i++) { naive60.step(0.2, 0.01); naive80.step(0.6, 0.01); }
        CHECK(near(float(naive60.lever), 0.f), "naive 60%%: lever moved to %f (expected stuck at idle)", naive60.lever);
        CHECK(near(float(naive80.lever), 1.f), "naive 80%%: lever reached %f (expected pinned at full)", naive80.lever);
    }
    {
        // The servo, driving the game's own law. Hold the stick at a series of positions and the lever must get
        // there and STAY there.
        ThrottleServo s;
        GameLever game;
        s.begin(0.f);
        const float dt = 0.01f;   // the board's 100 Hz loop
        auto hold = [&](float target, float seconds) {
            int n = int(seconds / dt);
            for (int i = 0; i < n; i++) game.step(s.step(target, dt), dt);
        };
        for (float target : { 0.40f, 0.75f, 0.55f, 1.00f, 0.00f, 0.62f })
        {
            hold(target, 4.0f);
            CHECK(near(float(game.lever), target, 0.02f),
                  "servo to %.2f: the game's lever came to rest at %.4f", target, game.lever);
            CHECK(near(s.lever(), target, 0.02f), "the board's model says %.4f", s.lever());
            // ...and it does not hunt: another second at the same target must not move it.
            double was = game.lever;
            hold(target, 1.0f);
            CHECK(near(float(game.lever), float(was), 0.01f), "it hunts about %.2f (%.4f -> %.4f)", target, was, game.lever);
        }
    }
    {
        // Arriving at a stop re-synchronises the model, which is the claim that makes open-loop dead reckoning
        // acceptable here. Start the model deliberately WRONG (as if the touch tray had moved the lever), go to
        // full, and the error must be gone.
        ThrottleServo s;
        GameLever game;
        s.begin(0.9f);            // the board thinks the lever is at 90%
        game.lever = 0.1;         // it is really at 10%
        for (int i = 0; i < 600; i++) game.step(s.step(1.0f, 0.01f), 0.01f);
        CHECK(near(float(game.lever), 1.f, 0.01f), "full throttle did not reach the stop (%f)", game.lever);
        CHECK(near(s.lever(), 1.f, 0.01f), "the model did not re-sync at the stop (%f)", s.lever());
        for (int i = 0; i < 600; i++) game.step(s.step(0.0f, 0.01f), 0.01f);
        CHECK(near(float(game.lever), 0.f, 0.01f), "idle did not reach the stop (%f)", game.lever);
    }
    {
        // Every command must be outside the game's dead zone or inside it by intent: a command between 0 and
        // 0.25 would be a lie -- the board would think it was moving the lever and the game would ignore it.
        ThrottleServo s;
        s.begin(0.f);
        for (float target : { 0.01f, 0.03f, 0.5f, 1.0f })
        {
            float c = s.step(target, 0.01f);
            CHECK(c == 0.f || std::fabs(c) >= ThrottleServo::kDeadZone,
                  "command %f for target %f falls inside the game's dead zone", c, target);
        }
    }
    {
        // A tiny error leaves the lever alone (command exactly zero), which is what makes the game HOLD.
        ThrottleServo s;
        s.begin(0.5f);
        CHECK(s.step(0.5f, 0.01f) == 0.f, "a settled servo must command exactly zero");
        CHECK(s.step(0.504f, 0.01f) == 0.f, "inside kSettled must command exactly zero");
    }
}

int main()
{
    std::printf("Aero RC Link -- host tests (the pure half; nothing here touches a pin)\n\n");
    testPpm();
    testSbus();
    testDetect();
    testCal();
    testFailsafe();
    testMaps();
    testThrottleServo();
    std::printf("\n%d checks, %d failed\n", gRun, gFail);
    return gFail == 0 ? 0 : 1;
}

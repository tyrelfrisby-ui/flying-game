// AERO RC LINK -- the channel-stream decoders: PPM and SBUS, with format auto-detection.
//
// Owner, 2026-11-10, choosing the shape of the RC link: "The board sits on the transmitter's trainer port, decodes
// the channel stream and presents itself as a BLE gamepad."  This header is the "decodes the channel stream" half,
// and it is the half that is true in EVERY shape the link could take, so it is deliberately pure: no Arduino, no
// ESP-IDF, no hardware. It is fed timestamps and bytes and it hands back channel values in microseconds. That is
// what lets tools/rc-link/test/host_test.cpp compile and run it on the Mac with clang, which is the only part of
// this firmware that has been RUN at all (see the README: everything that touches a pin is untested).
//
// WHAT IS NOT HERE, and why:
//  * no polarity setting for PPM. A PPM frame is a train of short MARK pulses, and a channel's value is the time
//    from one mark to the next, not the width of a mark. Inverting the signal moves every mark by exactly one pulse
//    width (~300 us, constant), so rising-to-rising and falling-to-falling intervals come out IDENTICAL. Positive
//    and negative PPM therefore decode the same with one edge polarity, and the owner never has to know which his
//    transmitter puts out. (This is measured in the host test: InvertedPpmDecodesTheSame.)
//  * no channel-count setting. The sync gap delimits the frame, so the count falls out of the stream.
//  * no baud setting for SBUS. It is Futaba's and it is fixed: 100000 baud, 8E2, inverted.

#pragma once

#include <cstdint>
#include <cstddef>

namespace rclink {

// ---------------------------------------------------------------------------------------------------------------
// Shared limits. A trainer port is an unshielded wire next to a 2.4 GHz transmitter, so every one of these is a
// REJECTION test, not a clamp: a value outside the band means the frame was corrupted and the whole frame is
// dropped. Half a frame of garbage reaching the flight model is worse than a 22 ms gap, which the failsafe covers.
// ---------------------------------------------------------------------------------------------------------------

inline constexpr uint16_t kChMinUs = 700;    // a channel pulse position; the standard band is 1000..2000, and
inline constexpr uint16_t kChMaxUs = 2200;   // transmitters at extended travel reach ~900..2100. 700/2200 is slack
                                             // enough for any of them and tight enough to reject a mark pulse
                                             // (~300 us) or a sync gap read as a channel.
inline constexpr uint32_t kPpmSyncMinUs = 2700;    // the gap that ends a PPM frame. Must sit above kChMaxUs with
inline constexpr uint32_t kPpmSyncMaxUs = 60000;   // room to spare; above kPpmSyncMaxUs the link is dead, not idle.
inline constexpr uint8_t  kMinChannels  = 4;       // fewer than four is not a transmitter, it is noise.
inline constexpr uint8_t  kMaxChannels  = 18;      // SBUS's 16 proportional + ch17/ch18 digital.

/// One decoded frame. `us` is pulse position in microseconds for every channel, whatever the source format, so
/// everything downstream is written once. `failsafe` and `frameLost` are SBUS's own flags (always false on PPM --
/// PPM has no flags, it just stops, which is why the failsafe state machine in rc_map.h is driven by TIME as well).
struct Frame
{
    uint16_t us[kMaxChannels] = {};
    uint8_t  count = 0;
    bool     failsafe = false;
    bool     frameLost = false;

    bool valid() const { return count >= kMinChannels; }
};

// ---------------------------------------------------------------------------------------------------------------
// PPM -- the classic trainer-port signal.
//
// One frame: ch1 mark, ch2 mark, ... chN mark, then a long sync gap, then the next frame. The interval between
// consecutive marks IS the channel value (1000..2000 us), and the frame repeats about every 22.5 ms. Feed every
// edge of ONE polarity (see the header comment: either works) and this builds frames out of the intervals.
//
// Corruption handling: any interval that is neither a channel nor a sync gap (a glitch, a missed interrupt, a
// second transmitter's bleed) poisons the frame in progress, and the frame is dropped at the next sync gap rather
// than published short. The channel count is therefore stable; a radio that really changes its channel count
// (changing model memory) publishes the new count on the first clean frame.
// ---------------------------------------------------------------------------------------------------------------

class PpmDecoder
{
public:
    /// One edge, timestamped in microseconds (a free-running counter; wrap is handled by unsigned arithmetic).
    /// Returns true when this edge completed a valid frame, which is then in `frame()`.
    bool feedEdge(uint32_t tUs)
    {
        if (!_haveLast) { _haveLast = true; _lastUs = tUs; return false; }
        uint32_t dt = tUs - _lastUs;   // unsigned: correct across the 32-bit micros() wrap (~71 min)
        _lastUs = tUs;

        if (dt >= kPpmSyncMinUs)
        {
            // End of a frame. Publish it only if it is whole: enough channels, none rejected, and the gap itself
            // plausible. A gap longer than kPpmSyncMaxUs means we were not watching a live PPM stream at all.
            bool ok = !_poisoned && _n >= kMinChannels && dt <= kPpmSyncMaxUs;
            if (ok)
            {
                _frame.count = _n;
                for (uint8_t i = 0; i < _n; i++) _frame.us[i] = _pending[i];
                _frame.failsafe = false;
                _frame.frameLost = false;
                _frames++;
            }
            _n = 0;
            _poisoned = false;
            return ok;
        }

        if (dt < kChMinUs || dt > kChMaxUs) { _poisoned = true; return false; }   // not a channel, not a sync
        if (_n < kMaxChannels) _pending[_n++] = static_cast<uint16_t>(dt);
        else _poisoned = true;   // more marks than any radio sends: this is not PPM
        return false;
    }

    const Frame& frame() const { return _frame; }
    /// Valid frames published since construction -- the auto-detector's evidence.
    uint32_t frames() const { return _frames; }
    /// Forget the partial frame (used when detection parks this decoder and comes back to it).
    void reset() { _haveLast = false; _n = 0; _poisoned = false; }

private:
    Frame    _frame;
    uint16_t _pending[kMaxChannels] = {};
    uint32_t _lastUs = 0, _frames = 0;
    uint8_t  _n = 0;
    bool     _haveLast = false, _poisoned = false;
};

// ---------------------------------------------------------------------------------------------------------------
// SBUS -- Futaba's serial format, as it comes out of a trainer/module bay: 100000 baud, 8E2, INVERTED, 25 bytes
// every 7 ms (high speed) or 14 ms.
//
//   byte  0      0x0F, the start byte
//   bytes 1..22  16 channels of 11 bits, LSB first, packed with no padding
//   byte  23     flags: bit0 ch17, bit1 ch18, bit2 FRAME LOST, bit3 FAILSAFE
//   byte  24     the end byte: 0x00 on most radios; Futaba also sends 0x04/0x14/0x24/0x34 (its own frame counter
//                in the high nibble), so the low nibble is what is checked.
//
// 11-bit counts are not microseconds. The standard conversion is us = (counts - 992) * 5/8 + 1500, which puts
// SBUS's -100% (172) at 988 us and +100% (1811) at 2012 us -- i.e. the same numbers a PPM radio puts out, which is
// the whole point: everything above rc_decode.h sees microseconds and never learns which format it is reading.
// ---------------------------------------------------------------------------------------------------------------

inline constexpr uint16_t kSbusStart = 0x0F;
inline constexpr uint16_t kSbusMid   = 992;

/// 11-bit SBUS counts to microseconds: `1500 + (counts - 992) * 5 / 8`, integer, exactly the expression every SBUS
/// reference implementation uses. It is an approximation of the format's nominal scaling and not a measurement, so
/// it is deliberately the SAME approximation as everyone else's -- 172 comes out 988 and 1811 comes out 2011, both
/// comfortably inside kChMinUs..kChMaxUs, and the centre lands on exactly 1500. A smarter rounding here would gain
/// half a microsecond out of 1000 and lose the ability to compare numbers with any other SBUS tool.
inline uint16_t sbusToUs(uint16_t counts)
{
    int32_t d = static_cast<int32_t>(counts) - kSbusMid;
    return static_cast<uint16_t>(1500 + d * 5 / 8);
}

class SbusDecoder
{
public:
    /// One received byte. Returns true when it completed a valid frame, which is then in `frame()`.
    bool feedByte(uint8_t b)
    {
        if (_n == 0)
        {
            if (b != kSbusStart) return false;   // hunting for the start byte
            _buf[_n++] = b;
            return false;
        }
        _buf[_n++] = b;
        if (_n < 25) return false;

        _n = 0;
        uint8_t end = _buf[24];
        // Accept 0x00 and Futaba's counted end bytes (low nibble 0 or 4). Anything else means we locked onto the
        // middle of the stream, or this is not SBUS; drop it and hunt again.
        if ((end & 0x0F) != 0x00 && (end & 0x0F) != 0x04) return false;

        unpack();
        _frames++;
        return true;
    }

    /// A gap in the byte stream (>= ~2 ms) ends any partial frame: the next 0x0F is a real start byte, not a
    /// channel byte that happens to be 0x0F. Call it from the read loop when the UART goes quiet.
    void gap() { _n = 0; }

    const Frame& frame() const { return _frame; }
    uint32_t frames() const { return _frames; }
    void reset() { _n = 0; }

private:
    void unpack()
    {
        // 16 channels x 11 bits, LSB first, straight out of bytes 1..22. Written as a bit cursor rather than the
        // usual hand-unrolled shift soup because the soup is where SBUS parsers get channel 9 wrong.
        uint32_t bit = 0;
        for (uint8_t c = 0; c < 16; c++)
        {
            uint16_t v = 0;
            for (uint8_t k = 0; k < 11; k++, bit++)
                v |= static_cast<uint16_t>((_buf[1 + (bit >> 3)] >> (bit & 7)) & 1) << k;
            _frame.us[c] = sbusToUs(v);
        }
        // ch17 / ch18 are single bits. They are presented as full-travel channels so a switch on them reads like
        // any other switch channel downstream.
        _frame.us[16] = (_buf[23] & 0x01) ? 2000 : 1000;
        _frame.us[17] = (_buf[23] & 0x02) ? 2000 : 1000;
        _frame.count = 18;
        _frame.frameLost = (_buf[23] & 0x04) != 0;
        _frame.failsafe  = (_buf[23] & 0x08) != 0;
    }

    Frame    _frame;
    uint8_t  _buf[25] = {};
    uint8_t  _n = 0;
    uint32_t _frames = 0;
};

// ---------------------------------------------------------------------------------------------------------------
// AUTO-DETECTION -- which format is on the wire.
//
// The owner's brief: "Auto-detect which is present rather than making him flash a different build for each
// transmitter". This does, and without a jumper or a button.
//
// It detects in PHASES rather than listening to both at once, on purpose:
//  * an SBUS stream is 100 kbaud, so watching it with a GPIO edge interrupt is up to 50000 interrupts a second.
//    That is a measurable share of a core spent rejecting intervals, every frame, forever.
//  * SBUS and PPM arrive on the SAME trainer-port pin, and asking one pad to be both a UART RX and an edge
//    interrupt source is a question about the ESP32's GPIO matrix that cannot be answered without the board in
//    hand. Phases make the question not arise: in a UART phase the interrupt is detached, in a PPM phase the UART
//    is stopped.
//  * SBUS gets the first phase because it is the stream that would hurt in the other order.
//
// Lock needs kLockFrames consecutive-ish valid frames, not one: one 0x0F in a PPM stream is a coincidence, four
// 25-byte frames with good start and end bytes are not. Once locked, the loser is never run again until the lock
// is lost -- a full second with no frame -- so changing transmitters, or unplugging the lead, re-detects without
// a power cycle.
// ---------------------------------------------------------------------------------------------------------------

enum class Format : uint8_t { None = 0, Ppm = 1, Sbus = 2 };

inline const char* formatName(Format f)
{
    return f == Format::Ppm ? "PPM" : f == Format::Sbus ? "SBUS" : "none";
}

/// The phase/lock policy, pure so it can be tested without a radio. The glue in main.cpp owns the actual UART and
/// interrupt; this owns only the decision of what to listen to and when to declare a winner.
class FormatDetector
{
public:
    static constexpr uint32_t kPhaseMs  = 600;    // long enough for ~85 PPM frames or ~85 SBUS frames
    static constexpr uint32_t kLostMs   = 1000;   // a locked format with no frame for this long is unlocked
    static constexpr uint32_t kLockFrames = 4;

    /// Call at startup and whenever the lock is lost. Starts in the SBUS phase (see above).
    void begin(uint32_t nowMs) { _format = Format::None; _phase = Format::Sbus; _phaseAtMs = nowMs; _lastFrameMs = nowMs; }

    /// What to listen to right now. While unlocked this alternates; once locked it is the locked format.
    Format listening() const { return _format != Format::None ? _format : _phase; }
    Format format() const { return _format; }

    /// Call once per loop with the valid-frame counts of both decoders and the time.
    /// Returns true when `listening()` has changed, i.e. the glue must re-wire the pin.
    bool update(uint32_t nowMs, uint32_t ppmFrames, uint32_t sbusFrames)
    {
        Format was = listening();

        if (_format == Format::None)
        {
            uint32_t got = _phase == Format::Sbus ? sbusFrames - _sbusBase : ppmFrames - _ppmBase;
            if (got >= kLockFrames) { _format = _phase; _lastFrameMs = nowMs; }
            else if (nowMs - _phaseAtMs >= kPhaseMs)
            {
                _phase = _phase == Format::Sbus ? Format::Ppm : Format::Sbus;
                _phaseAtMs = nowMs;
                _ppmBase = ppmFrames; _sbusBase = sbusFrames;
            }
        }
        else
        {
            uint32_t got = _format == Format::Sbus ? sbusFrames : ppmFrames;
            if (got != _lockedFrames) { _lockedFrames = got; _lastFrameMs = nowMs; }
            else if (nowMs - _lastFrameMs >= kLostMs)
            {
                // The transmitter was switched off, or the lead was pulled, or he changed radios. Hunt again.
                begin(nowMs);
                _ppmBase = ppmFrames; _sbusBase = sbusFrames;
            }
        }
        return listening() != was;
    }

private:
    Format   _format = Format::None, _phase = Format::Sbus;
    uint32_t _phaseAtMs = 0, _lastFrameMs = 0, _ppmBase = 0, _sbusBase = 0, _lockedFrames = 0;
};

}   // namespace rclink

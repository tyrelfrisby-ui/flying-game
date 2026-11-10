// AERO RC LINK -- calibration, channel order, failsafe, and the two output mappings.
//
// Pure C++: no Arduino, no hardware, so test/host_test.cpp runs all of it on the Mac. The glue in main.cpp owns
// the pins and the radios; this owns every decision that has a right and a wrong answer.
//
// ---------------------------------------------------------------------------------------------------------------
// THE TWO OUTPUT PATHS, and which one the owner actually wants
// ---------------------------------------------------------------------------------------------------------------
// Owner, 2026-11-10: "The board sits on the transmitter's trainer port, decodes the channel stream and presents
// itself as a BLE gamepad. The phone needs no special code -- it sees a standard controller."
//
// The first sentence is right and is what this firmware does. The second does not hold on iOS, and the evidence
// was already in this repo before today -- docs/RC-LINK-SPEC.md, written for the previous shape, says it in one
// line: "GameController only accepts Xbox, PlayStation, Switch Pro and MFi controllers". Unity on iOS reads pads
// through exactly that framework and nothing else (GamepadInput.cs: "the player's own GameController observer
// (GCControllerDidConnectNotification, compiled into UnityFramework) hands it to the Input System"). A home-made
// BLE HID gamepad pairs with an iPhone and then never appears to any app. See the README for the sources.
//
// So this header implements BOTH:
//
//  1. JOYLINK (`JoyLinkOut`) -- the path that needs no app change and is believed to work today, because the app
//     side already ships: an ESP32 standing in for the Mac on the `_aerojoy._udp` Bonjour service that
//     Assets/Plugins/iOS/JoyLink.mm already browses for and BuildScript.cs already declares in NSBonjourServices.
//     It carries 8 analogue channels and 32 switches, has an ABSOLUTE throttle, and its calibration wizard was
//     written for an RC radio in the first place (JoystickSetup.cs: "a 2- or 3-position switch channel",
//     "an RC radio's dongle sends its buttons and switches as axes"). Default.
//
//  2. BLE GAMEPAD (`GamepadOut`) -- what the owner asked for. Built so he can falsify the iOS claim in thirty
//     seconds instead of taking my word for it, and because it is the right answer on Android, Mac and Windows.
//     It can carry four sticks and no more (see the comments on `GamepadOut`).
// ---------------------------------------------------------------------------------------------------------------

#pragma once

#include <cstdint>
#include <cstddef>
#include "rc_decode.h"

namespace rclink {

// ---------------------------------------------------------------------------------------------------------------
// CHANNEL ORDER -- and the Mode 1 / Mode 2 correction
// ---------------------------------------------------------------------------------------------------------------
// The brief said: "Mode 1 vs Mode 2 matters -- most of the world flies Mode 2 (throttle left, elevator right) but
// it is a real setting and he may fly either; make it configurable rather than assumed."
//
// MEASURED ANSWER: there is nothing for the board to configure, and a Mode setting here would be a dial wired to
// nothing. Mode lives INSIDE the transmitter and decides which physical gimbal drives which FUNCTION. What leaves
// the trainer port is numbered by function, not by gimbal: channel 2 is the elevator on a Mode 1 radio and on a
// Mode 2 radio alike, because the radio has already done the mapping. The board cannot even tell which mode it is
// looking at, and neither path below is affected --
//
//   * `JoyLinkOut` sends the channels as they come and the game's wizard asks "PULL the stick fully BACK" and
//     finds the channel itself, so mode cannot be got wrong there even in principle;
//   * `GamepadOut` maps elevator to the right thumbstick and throttle to the left one BY FUNCTION, which is what
//     the game reads them as (GamepadInput.Read). A Mode 1 pilot's left hand moves the right thumbstick's pitch
//     axis, which is exactly what he wants, because he is flying the aeroplane, not the pad.
//
// A Mode setting is therefore NOT provided -- a knob that does nothing is worse than no knob, because one day
// somebody will turn it looking for a fix. What DOES differ by radio, and IS configurable, is the channel ORDER
// (`Order`, below, with a per-function override for anything odd).
//
// SENSES are deliberately not configurable here either: a transmitter has a servo-reverse switch per channel, and
// that is what it is for. A reverse in the board as well means two places to look when the elevator works
// backwards, and the owner will not spend an evening on that. The README says: fix the sense on the radio.

enum class Order : uint8_t
{
    AETR,   // aileron, elevator, throttle, rudder -- FrSky, Futaba, Jeti, most of OpenTX/EdgeTX. Default.
    TAER,   // throttle, aileron, elevator, rudder -- Spektrum and JR.
    RETA,   // rudder, elevator, throttle, aileron -- older Hitec/Multiplex.
};

/// Which 1-based channel carries each of the four flight functions. `of(Order)` covers every radio the owner is
/// likely to own; a radio with a hand-rolled channel order gets the four numbers set directly instead (that is the
/// override, and it is why this is a struct and not three more enum values).
struct FunctionChannels
{
    uint8_t aileron = 1, elevator = 2, throttle = 3, rudder = 4;

    static FunctionChannels of(Order o)
    {
        switch (o)
        {
            case Order::TAER: return { 2, 3, 1, 4 };
            case Order::RETA: return { 4, 2, 3, 1 };
            case Order::AETR:
            default:          return { 1, 2, 3, 4 };
        }
    }
};

inline const char* orderName(Order o) { return o == Order::TAER ? "TAER" : o == Order::RETA ? "RETA" : "AETR"; }

// ---------------------------------------------------------------------------------------------------------------
// CALIBRATION
// ---------------------------------------------------------------------------------------------------------------
// "Endpoints vary per transmitter, so there must be a calibration path -- at minimum a documented procedure,
// ideally a stored calibration on the board."
//
// There are two, and which one is needed depends on the output path, which is worth being blunt about because it
// decides how much work the owner has to do:
//
//   * ON THE JOYLINK PATH THERE IS NOTHING TO CALIBRATE ON THE BOARD. The board sends each channel against a
//     FIXED 1000/1500/2000 us reference, and the game's own wizard (OPTIONS > JOYSTICK SETUP) then measures the
//     real travel, the real centre, and the dead zone from the measured noise, per control, and stores it per
//     device name. That wizard already exists, already ships on iOS, and was written for exactly this. Putting a
//     second calibration in the board would mean two places where the elevator's travel is remembered and a
//     guarantee that one day they disagree. So: identity transform, fixed reference, let the wizard do its job.
//   * ON THE BLE GAMEPAD PATH THERE IS NO WIZARD, because the game has nothing to calibrate a gamepad with (it
//     is named controls, no profile -- that is the entire point of GamepadInput). So the board must do it, and
//     `Cal` below is what it stores in NVS.
//
// `Cal` is used by both paths; on the JoyLink path it simply holds its defaults.

struct Cal
{
    uint16_t lo = 1000, mid = 1500, hi = 2000;

    /// Sane after a calibration sweep? What is checked is that the channel MOVED (200 us of travel is a stick or a
    /// switch; anything less is a channel he never touched) and that the measured rest position lies inside that
    /// travel.
    ///
    /// What is deliberately NOT checked: that the centre is somewhere near the middle. A THROTTLE rests at the end
    /// of its travel, not in the middle of it, and the first version of this did demand a central centre -- which
    /// quietly threw away the calibration of the one channel that matters most and left the throttle on its
    /// defaults. The host test (`testCal`) sweeps a throttle resting at idle for exactly this reason.
    bool plausible() const
    {
        return hi >= lo + 200 && mid >= lo && mid <= hi && lo >= kChMinUs && hi <= kChMaxUs;
    }

    /// -1..+1 about the centre, with each half scaled on its own. Each half separately because a transmitter's
    /// centre is NOT the midpoint of its travel (sub-trim, and the throttle's centre is meaningless), and scaling
    /// both halves by one factor would make full left and full right different sizes.
    float bipolar(uint16_t us) const
    {
        if (us >= mid) { uint16_t span = hi > mid ? hi - mid : 1; float v = float(us - mid) / span; return v > 1.f ? 1.f : v; }
        uint16_t span = mid > lo ? mid - lo : 1;
        float v = -float(mid - us) / span;
        return v < -1.f ? -1.f : v;
    }

    /// 0..1 across the whole travel -- a throttle, or a switch channel's position.
    float unipolar(uint16_t us) const
    {
        uint16_t span = hi > lo ? hi - lo : 1;
        if (us <= lo) return 0.f;
        if (us >= hi) return 1.f;
        return float(us - lo) / span;
    }
};

/// A calibration sweep in progress: feed it frames while the owner stirs every stick and flips every switch.
/// `centre()` is called at the end with everything released, which is the only way to get the real centre --
/// the midpoint of the travel is not it (sub-trim).
class CalSweep
{
public:
    void begin()
    {
        for (uint8_t c = 0; c < kMaxChannels; c++) { _min[c] = 0xFFFF; _max[c] = 0; }
        _frames = 0;
    }

    void feed(const Frame& f)
    {
        for (uint8_t c = 0; c < f.count && c < kMaxChannels; c++)
        {
            uint16_t us = f.us[c];
            if (us < kChMinUs || us > kChMaxUs) continue;
            if (us < _min[c]) _min[c] = us;
            if (us > _max[c]) _max[c] = us;
        }
        _frames++;
    }

    /// Finish: `rest` is a frame taken with the sticks released. Writes `out[0..kMaxChannels)`; returns how many
    /// channels came out plausible. An implausible channel is left at its default, which is the safe answer --
    /// a channel that was never moved is a switch he did not touch, not a broken stick.
    uint8_t finish(const Frame& rest, Cal* out) const
    {
        uint8_t good = 0;
        for (uint8_t c = 0; c < kMaxChannels; c++)
        {
            Cal k;
            k.lo = _min[c];
            k.hi = _max[c];
            k.mid = (c < rest.count && rest.us[c] >= kChMinUs && rest.us[c] <= kChMaxUs) ? rest.us[c]
                                                                                         : uint16_t((k.lo + k.hi) / 2);
            // Clamp the centre inside the measured travel: a stick released a frame too late reads outside it.
            if (k.mid < k.lo) k.mid = k.lo;
            if (k.mid > k.hi) k.mid = k.hi;
            if (k.plausible()) { out[c] = k; good++; }
        }
        return good;
    }

    uint32_t frames() const { return _frames; }

private:
    uint16_t _min[kMaxChannels] = {}, _max[kMaxChannels] = {};
    uint32_t _frames = 0;
};

// ---------------------------------------------------------------------------------------------------------------
// FAILSAFE -- "safety-relevant, not polish"
// ---------------------------------------------------------------------------------------------------------------
// What actually goes wrong: the owner is inverted at the top of a loop, the lead falls out of the trainer port or
// the transmitter browns out, and the aeroplane keeps whatever it was holding. Full aileron held through a dropout
// rolls it into the ground while he is looking at the transmitter, not at the iPad.
//
// The decision, and what was rejected:
//
//   HOLD EVERYTHING -- rejected. This is the thing that kills the aeroplane. It is also the default of most RC
//   receivers, which is why it is worth saying no to explicitly.
//
//   CUT EVERYTHING TO IDLE AND CENTRE -- rejected for the throttle. A dropout is usually 100-300 ms (a BLE hiccup,
//   a Wi-Fi roam, a loose plug jiggled back in). Chopping the power at the top of a vertical turns a 200 ms
//   nothing into a tail slide. Centring the SURFACES, on the other hand, is right at any attitude.
//
//   STOP SENDING, so the game sees the controller go away -- rejected, and this is the subtle one. On the JoyLink
//   path the game drops `JoyLink.Connected` after half a second with no packet, and on the gamepad path
//   `GamepadInput` reports not-connected; either flips `TouchFlightControls.TouchSticksHidden` and the on-screen
//   sticks reappear under a thumb that is not there. Mid-sequence, the controls changing shape is worse than the
//   controls going quiet. So the board KEEPS SENDING through a dropout and sends a deliberate, safe frame.
//
//   THE DECISION:
//     * LIVE   -- frames arriving: pass them through.
//     * LOST   -- no frame for 100 ms (4 PPM frames or 14 SBUS frames), or SBUS's failsafe/frame-lost flag set:
//                 aileron, elevator and rudder to CENTRE; throttle HELD at its last value. The aeroplane goes to
//                 its trimmed attitude with the power it had, which is the hands-off behaviour the flight model
//                 already gets right (stick-free stability is in Aircraft.FreeFloat and the spin work leans on
//                 it). It keeps flying, and it keeps flying the same way for the 200 ms the link usually needs.
//     * DEAD   -- still nothing after 2 s: throttle to IDLE as well. By two seconds he is not flying it, and a
//                 glider descent is the least bad end to an unattended aeroplane.
//   And in both LOST and DEAD the board shouts: red LED, and the status line says so.
//
// The flags are honoured as well as the clock because SBUS tells the truth faster than a timeout can: a receiver
// in failsafe keeps sending 25-byte frames at full rate with the failsafe bit set, so the clock would never fire.

enum class Link : uint8_t { Live = 0, Lost = 1, Dead = 2 };

inline const char* linkName(Link l) { return l == Link::Live ? "LIVE" : l == Link::Lost ? "LOST" : "DEAD"; }

class Failsafe
{
public:
    static constexpr uint32_t kLostMs = 100;
    static constexpr uint32_t kDeadMs = 2000;

    void begin(uint32_t nowMs) { _lastGoodMs = nowMs; _state = Link::Lost; _haveThrottle = false; }

    /// Call with every decoded frame. A frame whose failsafe or frame-lost flag is set is NOT a good frame.
    void onFrame(uint32_t nowMs, const Frame& f)
    {
        if (f.valid() && !f.failsafe && !f.frameLost) _lastGoodMs = nowMs;
    }

    /// Call every loop. Returns the state, which the glue prints when it changes.
    Link update(uint32_t nowMs)
    {
        uint32_t age = nowMs - _lastGoodMs;
        _state = age >= kDeadMs ? Link::Dead : age >= kLostMs ? Link::Lost : Link::Live;
        return _state;
    }

    Link state() const { return _state; }

    /// Apply the decision above to the four flight controls, in place. Not const: while the link is live this is
    /// also where the last good throttle is remembered, which is the thing LOST hands back.
    /// `ail`, `ele`, `rud` are -1..1; `thr01` is 0..1 absolute.
    void apply(float& ail, float& ele, float& rud, float& thr01)
    {
        if (_state == Link::Live) { _lastThr = thr01; _haveThrottle = true; return; }
        ail = ele = rud = 0.f;
        if (_state == Link::Dead) { thr01 = 0.f; return; }
        thr01 = _haveThrottle ? _lastThr : 0.f;   // never flown yet: idle, not a guess
    }

private:
    uint32_t _lastGoodMs = 0;
    Link     _state = Link::Lost;
    float    _lastThr = 0.f;
    bool     _haveThrottle = false;
};

// ---------------------------------------------------------------------------------------------------------------
// OUTPUT 1: JOYLINK -- the shipping path, 8 analogue channels and 32 switches
// ---------------------------------------------------------------------------------------------------------------
// The packet is built in joylink_packet.h. This decides what goes in it.
//
// AXES 1..8 = channels 1..8, bipolar -1..1 against the FIXED reference (see CALIBRATION above). The game's
//   JoyAxis then re-scales with the endpoints its wizard measured, and a throttle is a JoyAxis with
//   Centering = false, so an RC throttle sitting at 40% is read as 40% power and stays there. That is the thing a
//   gamepad cannot do (see GamepadOut).
//
//   Eight is the ceiling, not a choice: JoystickInput.AxisCount is 8 and JoyLink.mm's receiver checks the packet
//   length against 8 axes. SBUS's channels 9..16 cannot be axes. They can be switches, below.
//
// BUTTONS 4..15 = channels 5..16, "on" above 20% of travel above centre.
//   Why not buttons 0..3 from channels 1..4: the wizard's button steps take the FIRST button it sees pressed
//   (JoystickInput.AnyButtonDown), so a stick deflected while he reads the prompt would be assigned as the GUNS
//   button. Leaving 0..3 permanently unset makes that impossible. Channels 5..16 are switches and knobs, which is what a
//   button step is looking for anyway, and this recovers SBUS's channels 9..16 as switches -- 12 switches, close
//   enough to the 13 named actions that he will run out of switches on the radio first.
//   A three-position switch loses its middle as a button; that is what the profile's FlapSwitch (a JoyAxis with
//   Centering = false and a three-way read) is for, and the wizard has a step for it.

inline constexpr uint8_t kJoyAxes = 8;        // JoystickInput.AxisCount
inline constexpr uint8_t kJoyButtons = 32;    // JoystickInput.ButtonCount
inline constexpr uint8_t kFirstSwitchChannel = 5;
inline constexpr uint8_t kFirstSwitchButton = 4;
inline constexpr float   kSwitchOnFrac = 0.2f;

struct JoyLinkOut
{
    float    axes[kJoyAxes] = {};
    uint32_t buttons = 0;

    static JoyLinkOut from(const Frame& f, const Cal* cal)
    {
        JoyLinkOut o;
        for (uint8_t a = 0; a < kJoyAxes; a++)
            o.axes[a] = a < f.count ? cal[a].bipolar(f.us[a]) : 0.f;

        for (uint8_t c = kFirstSwitchChannel; c <= 16 && c <= f.count; c++)
        {
            uint8_t b = uint8_t(kFirstSwitchButton + (c - kFirstSwitchChannel));
            if (b >= kJoyButtons) break;
            if (cal[c - 1].bipolar(f.us[c - 1]) > kSwitchOnFrac) o.buttons |= uint32_t(1) << b;
        }
        return o;
    }
};

// ---------------------------------------------------------------------------------------------------------------
// OUTPUT 2: BLE GAMEPAD -- what the owner asked for, and what it cannot carry
// ---------------------------------------------------------------------------------------------------------------
// Read GamepadInput.Read() in unity/Assets/_Project/Scripts/Bridge/GamepadInput.cs: everything the game can get
// from a pad is there, and it is
//
//      rightStick.x/y   roll, pitch        (through GamepadShaping.Stick: radial dead zone 0.02, expo 0.65)
//      leftStick.x      rudder             (through GamepadShaping.Rudder, SUMMED with the triggers)
//      leftStick.y      throttle AS A RATE (through GamepadShaping.ThrottleRate, dead zone 0.25)
//      leftTrigger/rightTrigger            rudder again -- RT minus LT, summed into the same rudder
//      4 face buttons, 2 shoulders, 2 stick clicks, 4 d-pad, Start, Select
//
// Three consequences, all of them measured off that file rather than assumed:
//
//  (a) FOUR ANALOGUE CHANNELS, NOT SIX. The triggers look free and are not: Rudder() adds (RT - LT) to the rudder,
//      so an aux channel parked on a trigger is a standing rudder deflection. An RC transmitter's two knobs have
//      nowhere analogue to go.
//
//  (b) THE THROTTLE IS A RATE, NOT A POSITION, and this is the real mismatch. A pad's left stick springs back to
//      centre, so the game integrates it: `Lever(current, stickRate, ...)` with full travel in
//      ThrottleIdleToFullS = 2.0 s and a 0.25 dead zone. An RC throttle stick does NOT spring back; it sits
//      wherever it is put. Map it straight onto leftStick.y and the aeroplane does this: the stick anywhere above
//      centre + 25% ramps the power to FULL and keeps it there; anywhere below, to idle; and in the middle band,
//      nothing. A four-position throttle. `ThrottleServo` below is the fix that needs no app change, and the
//      README gives the one-line app change that would be better.
//
//  (c) NO GEAR, and no second expo. `Frame` in GamepadInput has no gear field at all -- the gear/water-rudder slot
//      is reached from the touch tray and from JoystickInput's "gear" action, not from a pad. And the stick goes
//      through GamepadShaping's 0.65 expo on top of whatever expo is dialled into the transmitter, which for an
//      aerobatic pilot's own curves is not a small thing. Neither applies on the JoyLink path.

/// A gamepad frame, in the units an HID report wants: axes -32767..32767, triggers 0..255.
struct GamepadOut
{
    int16_t  lx = 0, ly = 0, rx = 0, ry = 0;
    uint8_t  lt = 0, rt = 0;     // always 0: see (a) above -- the triggers are a second rudder, so nothing is put
                                 // on them. They exist in the report because a standard pad has them.
    uint16_t buttons = 0;

    /// Channels to a pad. `thrStick` is -1..1 and comes from `ThrottleServo`, NOT from the throttle channel
    /// directly -- see (b). There is no mode parameter; see the Mode note at the top of this file.
    static GamepadOut from(const Frame& f, const Cal* cal, FunctionChannels fc, float thrStick)
    {
        GamepadOut g;
        // By FUNCTION, matching what the game reads each axis as (GamepadInput.Read):
        g.rx = q(ch(f, cal, fc.aileron));    // right stick X  -> roll
        g.ry = q(ch(f, cal, fc.elevator));   // right stick Y  -> pitch
        g.lx = q(ch(f, cal, fc.rudder));     // left stick X   -> rudder
        g.ly = q(thrStick);                  // left stick Y   -> throttle, as the RATE the game expects

        for (uint8_t c = kFirstSwitchChannel; c <= 16 && c <= f.count; c++)
        {
            uint8_t b = uint8_t(c - kFirstSwitchChannel);
            if (b >= 16) break;
            if (cal[c - 1].bipolar(f.us[c - 1]) > kSwitchOnFrac) g.buttons |= uint16_t(1) << b;
        }
        return g;
    }

private:
    static float ch(const Frame& f, const Cal* cal, uint8_t oneBased)
    {
        return (oneBased >= 1 && oneBased <= f.count) ? cal[oneBased - 1].bipolar(f.us[oneBased - 1]) : 0.f;
    }
    static int16_t q(float v)
    {
        if (v > 1.f) v = 1.f;
        if (v < -1.f) v = -1.f;
        return int16_t(v * 32767.f);
    }
};

// ---------------------------------------------------------------------------------------------------------------
// THROTTLE SERVO -- an absolute RC throttle through a gamepad axis the game integrates
// ---------------------------------------------------------------------------------------------------------------
// Consequence (b) above, solved in the board so the app stays untouched.
//
// The game's law is known exactly, and it is pure arithmetic in src/FlyingGame.Core/GamepadShaping.cs:
//
//      rate   = DeadZone(leftStick.y, 0.25)                  -- zero inside the zone, rescaled outside it
//      lever += rate * (|full - idle| / 2.0 s) * dt          -- GamepadShaping.Lever, clamped to the ends
//
// So the board keeps its OWN copy of that integrator, dead-reckoning where the game's lever has got to, and
// commands whatever deflection drives its model towards the position of the owner's actual throttle stick. The
// board is a servo amplifier and the game's lever is the servo.
//
// Why open-loop dead reckoning is good enough, which is the fair question:
//   * the law is deterministic and has no hidden state, so the two integrators agree to within timing jitter;
//   * BOTH SATURATE AT THE ENDS. Full throttle and idle drive the model and the game's lever hard against the
//     same stop, and any accumulated error is wiped. An aerobatic sequence is full power and idle, over and over,
//     so it re-syncs constantly -- which is why this is a worse idea for a cruise sim than it is here;
//   * worst case the lever sits a few percent off where the stick says, which is what a real throttle cable with
//     a stretched sheath does anyway.
// Where it is wrong, and this is written down rather than hidden: if the touch tray or a keyboard also moves the
// lever, the model does not know, and the error persists until the next time he goes to a stop. It cannot be
// fixed from the board -- the game would have to report the lever back, which is the custom protocol the owner
// rejected. The app-side alternative in the README (one `if` in GamepadInput.Read) has none of this.

class ThrottleServo
{
public:
    static constexpr float kDeadZone   = 0.25f;   // GamepadShaping.ThrottleStickDeadZone
    static constexpr float kIdleToFullS = 2.0f;   // GamepadShaping.ThrottleIdleToFullS
    /// Below this error the lever is left alone: a commanded zero is what makes the game's lever HOLD.
    static constexpr float kSettled = 0.01f;
    /// The error at which full deflection is commanded. Below it the command eases off, so the lever arrives
    /// instead of hunting about the target by one loop's worth of travel.
    static constexpr float kProportionalBand = 0.08f;

    /// `lever01` is where the model thinks the game's lever is; start it at idle, which is where a fresh scene is.
    void begin(float lever01 = 0.f) { _model = clamp01(lever01); }

    /// One step. `target01` is the owner's throttle stick, 0..1. `dt` seconds since the last call.
    /// Returns the leftStick.y to send, -1..1.
    float step(float target01, float dt)
    {
        if (dt <= 0.f) return _cmd;
        float target = clamp01(target01);
        float err = target - _model;
        float mag = err < 0 ? -err : err;

        // AT A STOP, COMMAND THE STOP -- whatever the model believes.
        //
        // This is the line that makes open-loop dead reckoning honest, and the first version did not have it. With
        // only the proportional law below, a model that had drifted HIGH would reach 1.0 itself and stop
        // commanding while the game's lever was still at 20%, and the error would never be corrected -- the
        // host test (`full throttle did not reach the stop`) caught it leaving the lever at 0.19. Driving into a
        // stop costs nothing (the game clamps the lever at the end), and it is the only feedback-free way back
        // into agreement. An aerobatic sequence is full power and idle over and over, so this fires constantly.
        bool atFull = target >= 1.f - kSettled, atIdle = target <= kSettled;
        float want;
        bool up;
        if (atFull || atIdle) { want = 1.f; up = atFull; }
        else if (mag < kSettled) { _cmd = 0.f; return _cmd; }
        else
        {
            // The rate we want, as a fraction of full: proportional inside the band, saturated outside it.
            want = mag / kProportionalBand;
            if (want > 1.f) want = 1.f;
            up = err > 0.f;
        }

        // Lift it past the dead zone the game will take off again. GamepadShaping.DeadZone maps
        // |x| -> (|x| - dz) / (1 - dz), so to ask for a rate `want` we must send dz + want * (1 - dz). This is
        // GamepadShaping.PreCompensateDeadZone, by the same reasoning and for the same reason.
        float x = kDeadZone + want * (1.f - kDeadZone);
        _cmd = up ? x : -x;

        // Advance the model by exactly what the game will do with that command this step. Note that the stop case
        // above advances it at the SAME rate rather than snapping it to the stop: snapping would be a lie for the
        // two seconds the lever actually takes to travel, and a stop tapped and released would leave the model
        // believing a lever position the game never reached.
        _model = clamp01(_model + want * (1.f / kIdleToFullS) * dt * (up ? 1.f : -1.f));
        return _cmd;
    }

    /// Where the model believes the game's lever is -- printed on the status line so a drift is visible.
    float lever() const { return _model; }

private:
    static float clamp01(float v) { return v < 0.f ? 0.f : v > 1.f ? 1.f : v; }
    float _model = 0.f, _cmd = 0.f;
};

}   // namespace rclink

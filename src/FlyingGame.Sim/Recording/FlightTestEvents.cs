using System;
using FlyingGame.Core;
using FlyingGame.Core.Recording;
using FlyingGame.Sim.Replay;

namespace FlyingGame.Sim.Recording;

/// <summary>
/// SPOTS THE MOMENTS WORTH MARKING, once per physics step, with no allocation.
///
/// Some of what a flight-test reader wants is already a C# event on the Aircraft — HardImpact, ComponentLost, the
/// contact-damage Contact event — and those are wired straight to <see cref="FlightTestRecorder.Mark"/> by the
/// Bridge. The rest has no event and has to be watched for: a stall, a spin, wheels on and off the ground.
///
/// THE THRESHOLDS ARE NOT NEW. They are the ones the rest of the game already uses
/// (<see cref="FlightEventLog"/>): stalled above 0.35 and flying again below 0.20, autorotation at 0.6 rad/s of yaw
/// held for two seconds. Inventing a second set would mean the recorder's idea of a spin disagreed with the
/// debrief's and the syllabus's, and two answers to "was that a spin" is worse than either.
///
/// Unlike FlightEventLog, which keeps a growing List of events carrying a string Detail, this writes nothing but
/// bits: it is called 200 times a second and must stay blittable and allocation-free.
/// </summary>
public sealed class FlightTestEvents
{
    private bool _stalled;
    private bool _spinning;
    private double _spinSince = -1;
    private double _spinCalmSince = -1;
    private bool _wasTouching;
    private bool _wasRunning = true;
    private double _t;

    /// <summary>Watch one step. Returns the events that began on it, to be handed to the recorder.</summary>
    public FlightRecording.Event Step(Aircraft a, double dt)
    {
        if (a == null) return FlightRecording.Event.None;
        _t += dt;
        FlightRecording.Event fired = FlightRecording.Event.None;

        // STALL, with the repo's hysteresis: once stalled it takes a lower number to be flying again, so a wing
        // hovering on the threshold does not machine-gun the event channel and trigger a burst every other step.
        double frac = a.StalledFraction;
        bool stalled = _stalled ? frac > FlightEventLog.StalledExit : frac >= FlightEventLog.StalledEnter;
        if (stalled && !_stalled) fired |= FlightRecording.Event.Stall;
        _stalled = stalled;

        // SPIN: stalled AND yawing, held. The dwell is what separates a spin from a stall with a wing drop, and it
        // is why the entry is dated at the moment autorotation STARTED rather than when the dwell expired — by the
        // time you can be sure it is a spin, the interesting part has already happened.
        double yaw = Math.Abs(a.State.Rates.Z);
        bool rotating = stalled && yaw >= FlightEventLog.SpinYawRateRadS;
        if (rotating)
        {
            _spinCalmSince = -1;
            if (_spinSince < 0) _spinSince = _t;
            if (!_spinning && _t - _spinSince >= FlightEventLog.SpinDwellSec)
            {
                _spinning = true;
                fired |= FlightRecording.Event.SpinEntry;
            }
        }
        else
        {
            _spinSince = -1;
            if (_spinning)
            {
                if (_spinCalmSince < 0) _spinCalmSince = _t;
                if (_t - _spinCalmSince >= FlightEventLog.SpinExitSec)
                {
                    _spinning = false;
                    _spinCalmSince = -1;
                    fired |= FlightRecording.Event.SpinExit;
                }
            }
        }

        // TOUCHDOWN and lift-off. Which surface it was comes from the leg's own channels in the file; the event
        // channel only says that the aeroplane met something.
        bool touching = a.AnyGearTouching;
        if (touching && !_wasTouching)
        {
            fired |= FlightRecording.Event.Touchdown;
            fired |= OverWater(a) ? FlightRecording.Event.WaterContact : FlightRecording.Event.GroundContact;
        }
        _wasTouching = touching;

        bool running = a.EngineRunning;
        if (!running && _wasRunning) fired |= FlightRecording.Event.EngineStop;
        _wasRunning = running;

        return fired;
    }

    /// <summary>Indexed rather than foreach: Wheels is an IReadOnlyList over an array and iterating the interface
    /// boxes an enumerator, which at 200 Hz is a steady drip of garbage into the physics step.</summary>
    private static bool OverWater(Aircraft a)
    {
        System.Collections.Generic.IReadOnlyList<WheelState> w = a.Wheels;
        if (w == null) return false;
        for (int i = 0; i < w.Count; i++) if (w[i] != null && w[i].InContact && w[i].OverWater) return true;
        return false;
    }

    public void Reset()
    {
        _stalled = _spinning = false;
        _spinSince = _spinCalmSince = -1;
        _wasTouching = false;
        _wasRunning = true;
        _t = 0;
    }
}

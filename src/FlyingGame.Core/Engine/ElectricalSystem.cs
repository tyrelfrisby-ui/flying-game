using System;

namespace FlyingGame.Core.Engine;

/// <summary>
/// THE BUS, as a light aeroplane's four switches see it (Ty 2026-11-04 19:22: the Pitts' rear panel is MASTER,
/// ALTERNATOR, FUEL PUMP and SMOKE, "that's it"). One 14 V bus fed by a battery behind the master contactor and an
/// alternator whose field is switched separately, as on any certificated single:
///
///   MASTER off        the bus is dead. The engine runs on, because magnetos need no bus; the smoke and the boost
///                     pump stop, the ammeter reads zero.
///   ALTERNATOR off    the battery carries the load alone and runs down; once it is flat the bus is dead.
///   FUEL PUMP on      the electric boost pump: <see cref="FuelPumpAmps"/> on the bus. In this sim the engine-driven
///                     pump never fails (EngineReliability is not wired to the flying aeroplane), so the boost pump's
///                     only effect is its load; FailureCause.EngineFuelPumpFailure is where it will bite.
///   SMOKE on          the smoke-oil pump is electric too: no bus, no smoke (SmokeController reads <see cref="BusLive"/>).
///
/// The alternator comes on line above <see cref="AlternatorCutInRpm"/>. Values are (E) estimates for an
/// AEIO-360 single, not from a book: a 15 Ah battery, a 40 A alternator, 2 A of radios and instruments, 3 A for the
/// boost pump and 6 A for the smoke pump. Engine-free arithmetic, stepped once per sim step (Aircraft.Step).
/// </summary>
public sealed class ElectricalSystem
{
    public const double AlternatorVolts = 14.2, BatteryFullVolts = 12.6, BatteryFlatVolts = 11.6, DeadVolts = 10.5;
    public const double BatteryAh = 15.0, AlternatorMaxAmps = 40.0, AlternatorCutInRpm = 1000.0;
    public const double BaseAmps = 2.0, FuelPumpAmps = 3.0, SmokePumpAmps = 6.0;
    /// <summary>Charge current into a battery that is down, tapering to nothing as it fills.</summary>
    public const double MaxChargeAmps = 10.0;

    public bool Master = true, Alternator = true, FuelPump;
    /// <summary>The smoke pump is asking for current (the SMOKE switch, set by whoever owns the smoke).</summary>
    public bool SmokePump;

    /// <summary>The battery's charge, 0 (flat) .. 1 (full).</summary>
    public double Charge01 { get; private set; } = 1.0;
    public double BusVolts { get; private set; } = AlternatorVolts;
    /// <summary>The ammeter: current INTO the battery, + charging, − discharging. Zero with the master off.</summary>
    public double Amps { get; private set; }
    /// <summary>True while anything on the bus can run.</summary>
    public bool BusLive => BusVolts >= DeadVolts;
    /// <summary>The alternator is carrying the bus.</summary>
    public bool Charging { get; private set; }

    /// <summary>The battery's resting voltage at its charge (lead-acid: roughly linear between flat and full).</summary>
    public double BatteryVolts => BatteryFlatVolts + (BatteryFullVolts - BatteryFlatVolts) * Charge01;

    /// <summary>The load the bus is asked for, amps, whether or not it can be supplied.</summary>
    public double LoadAmps => BaseAmps + (FuelPump ? FuelPumpAmps : 0.0) + (SmokePump ? SmokePumpAmps : 0.0);

    public void Step(double dt, double engineRpm)
    {
        if (dt <= 0.0) return;
        if (!Master)
        {
            // The contactor is open: nothing on the bus, nothing in or out of the battery, and the alternator's
            // field is fed from the bus, so it is dark too.
            BusVolts = 0.0; Amps = 0.0; Charging = false;
            return;
        }
        double load = LoadAmps;
        Charging = Alternator && engineRpm >= AlternatorCutInRpm;
        double batteryAmps;
        if (Charging)
        {
            batteryAmps = Math.Min(MaxChargeAmps * (1.0 - Charge01), Math.Max(0.0, AlternatorMaxAmps - load));
            BusVolts = AlternatorVolts;
        }
        else
        {
            batteryAmps = Charge01 > 0.0 ? -load : 0.0;
            BusVolts = BatteryVolts;
        }
        Charge01 = Math.Clamp(Charge01 + batteryAmps * dt / (BatteryAh * 3600.0), 0.0, 1.0);
        if (!Charging && Charge01 <= 0.0) { BusVolts = 0.0; batteryAmps = 0.0; }
        Amps = batteryAmps;
    }

    /// <summary>For a test or a cold start: a battery at this charge.</summary>
    public void SetCharge(double charge01) => Charge01 = Math.Clamp(charge01, 0.0, 1.0);
}

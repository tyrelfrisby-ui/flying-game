using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// WHAT THE PROPELLER ACTUALLY DOES, measured before anything was changed (PROPPHYS=1). Ty 2026-11-11, about his own
/// aeroplane: "If the high pitch stop on a cs prop is set to redline rpm at vd (lets do that at 10,000 ft da) then
/// anything over that should be an over rev which is not happening now... The braking effect of a windmilling prop
/// should also be a function of low pitch stop and engine pumping and friction drag at idle. There should be a small
/// but noticeable difference between idle and mixture cutoff windmilling based on physics not a guess or estimation",
/// and "A stopped prop should have much lower drag than either idle windmilling or idle cutoff windmilling".
///
/// Four tables, one per report. Every number here is a MEASUREMENT of the model as it stands; nothing in this file
/// asserts, so it cannot go stale into a false green.
/// </summary>
public class PropPhysicsProbe
{
    private readonly ITestOutputHelper _o;
    public PropPhysicsProbe(ITestOutputHelper o) { _o = o; }

    private static string ConfigDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        return Path.Combine(d.FullName, "configs", "aircraft");
    }

    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(Path.Combine(ConfigDir(), id + ".json"));

    private static readonly string[] ConstantSpeed = { "pitts-s2b-like", "extra-300-like", "great-lakes-like" };

    /// <summary>ITEM 1. Does the governor ever run out of blade? Full throttle, prop lever forward, the speed swept
    /// past Vd at sea level and at 10,000 ft density altitude, with the steady torque balance solved at each point.</summary>
    [Fact]
    public void OverspeedSweep()
    {
        if (Environment.GetEnvironmentVariable("PROPPHYS") != "1") return;
        double rho10k = Atmosphere.DensityAtAltitude(3048.0);
        _o.WriteLine($"rho SL {Atmosphere.SeaLevelDensityKgM3:F4}  rho 10,000 ft {rho10k:F4}  sigma {rho10k / Atmosphere.SeaLevelDensityKgM3:F4}");
        foreach (string id in ConstantSpeed)
        {
            AircraftConfig c = Load(id);
            PropulsionConfig p = c.Propulsion!;
            double vne = c.Limits?.VneMs ?? 100.0, vd = 1.1 * vne;
            _o.WriteLine($"\n=== {id}: fine {p.FineStopDeg:F2} coarse {p.CoarseStopDeg:F2} deg, governed {p.GovernedRpm:F0}, red {p.MaxRpm:F0}, " +
                         $"Vne {vne * 1.94384:F0} kt, Vd {vd * 1.94384:F0} kt CAS");
            foreach ((string where, double rho) in new[] { ("sea level", Atmosphere.SeaLevelDensityKgM3), ("10,000 ft DA", rho10k) })
            {
                double tasPerCas = Math.Sqrt(Atmosphere.SeaLevelDensityKgM3 / rho);
                _o.WriteLine($"  {where} (TAS/CAS {tasPerCas:F3})");
                foreach (double fCas in new[] { 0.6, 0.8, 1.0, 1.1, 1.2, 1.4, 1.6 })
                {
                    double cas = fCas * vd, tas = cas * tasPerCas;
                    PropModel.PropDrive d = PropModel.SteadyDrive(p, 1.0, tas, rho);
                    bool onCoarse = d.BladePitchDeg >= p.CoarseStopDeg - 0.01;
                    _o.WriteLine($"    {fCas * 100,4:F0}% Vd  CAS {cas * 1.94384,5:F0} kt  TAS {tas * 1.94384,5:F0} kt | " +
                                 $"rpm {d.ShaftRpm,7:F1}  blade {d.BladePitchDeg,6:F2} deg{(onCoarse ? "  ON COARSE STOP" : "")}" +
                                 $"{(d.ShaftRpm > p.MaxRpm + 1.0 ? "   OVERSPEED" : "")}");
                }
            }
        }
    }

    /// <summary>ITEM 1b. What Ty's own rule asks of the stops, at sea level (where it is derived now) and at 10,000 ft
    /// density altitude (where he says to derive it), against what each type is authored with.</summary>
    [Fact]
    public void StopsFromTheRule()
    {
        if (Environment.GetEnvironmentVariable("PROPPHYS") != "1") return;
        double rho10k = Atmosphere.DensityAtAltitude(3048.0);
        double tasPerCas = Math.Sqrt(Atmosphere.SeaLevelDensityKgM3 / rho10k);
        _o.WriteLine("type                  authored fine/coarse | rule @ SL, Vd TAS=CAS | rule @ 10k DA, Vd CAS");
        foreach (string id in ConstantSpeed)
        {
            AircraftConfig c = Load(id);
            PropulsionConfig p = c.Propulsion!;
            double vne = c.Limits?.VneMs ?? 100.0, r = 0.5 * p.PropDiameterM;
            var sl = BladeElement.GovernorStops(p.PropBladeCount, r, p.ActivityFactor, 0.15, p.MaxPowerW, p.MaxRpm, vne);
            // At 10,000 ft DA the engine is weaker AND the true airspeed at the same CAS is higher; both belong.
            double qRated = p.MaxPowerW * PistonEngine.AltitudePowerFraction(rho10k / Atmosphere.SeaLevelDensityKgM3)
                            / (p.MaxRpm * 2.0 * Math.PI / 60.0);
            double coarse10k = BladeElement.PitchForTorqueDeg(p.PropBladeCount, r, p.ActivityFactor, 0.15, qRated,
                                                              p.MaxRpm, 1.1 * vne * tasPerCas, rho10k);
            _o.WriteLine($"{id,-22} {p.FineStopDeg,6:F2} {p.CoarseStopDeg,6:F2} | {sl.FineDeg,6:F2} {sl.CoarseDeg,6:F2} | coarse {coarse10k,6:F2}");
        }
    }

    /// <summary>ITEM 1c, AND THE SEAMS. The SIM, not the steady solve: the shaft integrated with the aeroplane held at
    /// a speed, so the governor's own rate law and the <c>MaxRpm * 1.6</c> clamp are in the answer. Held, because a
    /// free aeroplane cannot be kept at 140 % of Vd long enough to read a tachometer.</summary>
    [Fact]
    public void OverspeedInTheSim()
    {
        if (Environment.GetEnvironmentVariable("PROPPHYS") != "1") return;
        foreach (string id in ConstantSpeed)
        {
            AircraftConfig c = Load(id);
            PropulsionConfig p = c.Propulsion!;
            double vne = c.Limits?.VneMs ?? 100.0, vd = 1.1 * vne;
            _o.WriteLine($"\n=== {id} held at speed, full power, prop forward, 10,000 ft");
            foreach (double fCas in new[] { 0.8, 1.0, 1.1, 1.2, 1.4 })
            {
                double tas = fCas * vd * Math.Sqrt(Atmosphere.SeaLevelDensityKgM3 / Atmosphere.DensityAtAltitude(3048.0));
                var ac = Held(c, tas, 3048.0);
                for (int i = 0; i < 2000; i++) { ac.Step(new ControlInputs(0, 0, 0, -1.0), 1.0 / 200.0); Hold(ac, tas, 3048.0); }
                _o.WriteLine($"  {fCas * 100,4:F0}% Vd  TAS {tas * 1.94384,5:F0} kt | tach {ac.EngineRpm,7:F1}  blade {ac.BladePitchDeg,6:F2} deg" +
                             $"{(ac.EngineRpm > p.MaxRpm + 1.0 ? "   OVERSPEED" : "")}");
            }
        }
    }

    /// <summary>SEAM 1, the compression ratchet: a dead engine's propeller held in still air and in a glide, to see
    /// whether it ever actually stops.</summary>
    [Fact]
    public void DoesADeadPropellerStop()
    {
        if (Environment.GetEnvironmentVariable("PROPPHYS") != "1") return;
        foreach (string id in new[] { "pitts-s2b-like", "extra-300-like", "j3-cub-like" })
        {
            AircraftConfig c = Load(id);
            _o.WriteLine($"\n=== {id}");
            foreach (double v in new[] { 0.0, 25.0, 35.0 })
            {
                var ac = Held(c, Math.Max(v, 0.0), 1500.0);
                ac.MixtureLever = 0.0;                                  // idle cut-off
                for (int i = 0; i < 6000; i++) { ac.Step(new ControlInputs(0, 0, 0, 1.0), 1.0 / 200.0); Hold(ac, v, 1500.0); }
                _o.WriteLine($"  V {v,5:F1} m/s after 30 s: tach {ac.EngineRpm,8:F2}  shaft {ac.ShaftRpm,8:F2}  running {ac.EngineRunning}");
            }
        }
    }

    private static Aircraft Held(AircraftConfig c, double tas, double altM)
        => new(c, new RigidBodyState(new FlyingGame.Core.MathTypes.Vec3(0, 0, -altM), FlyingGame.Core.MathTypes.Quat.Identity,
                                     new FlyingGame.Core.MathTypes.Vec3(tas, 0, 0), FlyingGame.Core.MathTypes.Vec3.Zero),
               ControlDeflections.Neutral);

    private static void Hold(Aircraft ac, double tas, double altM)
        => ac.State = new RigidBodyState(new FlyingGame.Core.MathTypes.Vec3(0, 0, -altM), FlyingGame.Core.MathTypes.Quat.Identity,
                                         new FlyingGame.Core.MathTypes.Vec3(tas, 0, 0), FlyingGame.Core.MathTypes.Vec3.Zero);

    /// <summary>ITEMS 2-4. The three windmilling cases, measured as the propeller's own axial force: engine idling,
    /// engine dead and turning, propeller stopped.</summary>
    [Fact]
    public void WindmillAndStoppedDrag()
    {
        if (Environment.GetEnvironmentVariable("PROPPHYS") != "1") return;
        double rho = Atmosphere.SeaLevelDensityKgM3;
        foreach (string id in new[] { "pitts-s2b-like", "extra-300-like", "great-lakes-like", "j3-cub-like" })
        {
            AircraftConfig c = Load(id);
            PropulsionConfig p = c.Propulsion!;
            double disc = Math.PI * 0.25 * p.PropDiameterM * p.PropDiameterM;
            double oilLoss = PropModel.OilLossPitchDeg(p);
            _o.WriteLine($"\n=== {id}  cs={p.ConstantSpeed} fine {p.FineStopDeg:F2} coarse {p.CoarseStopDeg:F2} " +
                         $"oilLossPitch {oilLoss:F2}  fittedBlade {p.BladePitch75Deg:F2}  idleDragCd {p.IdleDragCd:F3}  disc {disc:F2} m2");
            double deadPitch = oilLoss > 0.0 ? oilLoss : p.BladePitch75Deg;
            foreach (double v in new[] { 30.0, 40.0, 50.0, 60.0, 80.0 })
            {
                var vel = new FlyingGame.Core.MathTypes.Vec3(v, 0, 0);
                var zero = FlyingGame.Core.MathTypes.Vec3.Zero;
                // IDLE, ENGINE RUNNING: the steady torque balance at the idle stop, with the governor alive.
                PropModel.PropDrive idle = PropModel.SteadyFlownDrive(p, 0.0, v, rho);
                var fIdle = PropModel.Compute(p, 0.0, vel, zero, rho, idle);
                // MIXTURE CUT-OFF: the engine is dead, the oil has gone, the blade is on the stop its spring takes it
                // to, and the rpm is where the air and the motoring engine balance.
                double wmRpm = PropModel.WindmillRpm(p, v, rho);
                bool turning = !double.IsNaN(wmRpm) && wmRpm > PropModel.StoppedRpm;
                var dead = PropModel.PropDrive.Dead(turning ? wmRpm : 0.0);
                if (turning) dead = dead.WithShaft(wmRpm, p.ConstantSpeed ? deadPitch : 0.0);
                var fDead = PropModel.Compute(p, 0.0, vel, zero, rho, dead);
                // STOPPED, at that same stop.
                var fStop = PropModel.Compute(p, 0.0, vel, zero, rho, PropModel.PropDrive.Dead(0.0));
                double q = 0.5 * rho * v * v * disc;
                _o.WriteLine($"  V {v,5:F1} m/s | idle rpm {idle.ShaftRpm,7:F1} blade {idle.BladePitchDeg,5:F2} X {fIdle.Force.X,8:F1} N Cd {-fIdle.Force.X / q,6:F3}" +
                             $" | cutoff rpm {(turning ? wmRpm : 0.0),7:F1} X {fDead.Force.X,8:F1} N Cd {-fDead.Force.X / q,6:F3}" +
                             $" | stopped X {fStop.Force.X,8:F1} N Cd {-fStop.Force.X / q,6:F3}");
            }
        }
    }

    /// <summary>ITEM 2, the ENGINE's share: what the engine is contributing at the idle stop, rpm by rpm, against the
    /// torque the blade is asking for. "Engine pumping and friction drag at idle" is a statement about this column.</summary>
    [Fact]
    public void WhatTheEngineDoesAtIdle()
    {
        if (Environment.GetEnvironmentVariable("PROPPHYS") != "1") return;
        double rho = Atmosphere.SeaLevelDensityKgM3;
        foreach (string id in new[] { "pitts-s2b-like", "j3-cub-like" })
        {
            AircraftConfig c = Load(id);
            PropulsionConfig p = c.Propulsion!;
            double pitch = p.ConstantSpeed ? p.FineStopDeg : p.BladePitch75Deg;
            var g = new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, pitch, p.ActivityFactor);
            double bleed = PistonEngine.IdleBleedThrottle(g, p.MaxPowerW, p.MaxRpm, p.IdleRpm, 1.0, rho, 1.0);
            _o.WriteLine($"\n=== {id} blade {pitch:F2} deg  idle stop throttle {bleed:F4}  MP {PistonEngine.ManifoldInHg(bleed, 1.0):F2} inHg " +
                         $"(closed {PistonEngine.ManifoldInHg(0.0, 1.0):F2}, friction intercept {PistonEngine.FrictionManifoldInHg:F1})");
            foreach (double rpm in new[] { 700.0, 1200.0, 1800.0, 2500.0 })
            {
                double qFire = PistonEngine.TorqueNm(p.MaxPowerW, p.MaxRpm, rpm, bleed, 1.0, 1.0);
                double qMotorDry = PistonEngine.MotoringTorqueNm(p, rpm, 0.0, 1.0, 1.0);
                double qMotorOiled = PistonEngine.MotoringTorqueNm(p.MaxPowerW, p.MaxRpm, rpm, 0.0, 1.0, 1.0, 0.0);
                double qBlade50 = BladeElement.Solve(g, 50.0, 0.0, rpm, rho).TorqueNm;
                _o.WriteLine($"   {rpm,5:F0} rpm | engine at the idle stop {qFire,7:F1} N.m | motoring, oiled {qMotorOiled,7:F1} | " +
                             $"motoring, dry {qMotorDry,7:F1} | blade at 50 m/s {qBlade50,8:F1}");
            }
        }
    }

    /// <summary>ITEM 4 at the speed it matters — each type's own approach speed, 1.3 Vso — with the blade's area as
    /// a fraction of the disc beside it, because that is the one number the stopped drag is linear in.</summary>
    [Fact]
    public void StoppedAgainstWindmillingAtApproachSpeed()
    {
        if (Environment.GetEnvironmentVariable("PROPPHYS") != "1") return;
        double rho = Atmosphere.SeaLevelDensityKgM3;
        _o.WriteLine("type                   blade area/disc |  pitch |  1.3Vso | stopped N    Cd | windmilling rpm      N    Cd | ratio");
        foreach (string id in new[] { "pitts-s2b-like", "extra-300-like", "great-lakes-like", "j3-cub-like", "j3-cub-floats-like" })
        {
            AircraftConfig c = Load(id);
            PropulsionConfig p = c.Propulsion!;
            double v = 1.3 * FlyingGame.Sim.Practice.PracticeScenario.EstimateVso(c, 0.0, 0.0);
            double radius = 0.5 * p.PropDiameterM, disc = Math.PI * radius * radius, q = 0.5 * rho * v * v * disc;
            double[] pitches = p.ConstantSpeed ? new[] { p.FineStopDeg, p.CoarseStopDeg } : new[] { p.BladePitch75Deg };
            foreach (double pitch in pitches)
            {
                var g = new BladeElement.Geometry(p.PropBladeCount, radius, pitch, p.ActivityFactor);
                // Blade area, straight off the chord family the whole blade-element model uses.
                double area = 0.0;
                for (int i = 0; i < 200; i++) { double x = 0.15 + (i + 0.5) * (1.0 - 0.15) / 200.0; area += g.ChordM(x) * (1.0 - 0.15) / 200.0 * radius; }
                area *= p.PropBladeCount;
                double EngineQ(double r) => PistonEngine.MotoringTorqueNm(p, r, 0.0, 1.0, 1.0);
                double stopped = BladeElement.StoppedDragN(g, v, rho);
                var w = BladeElement.Windmill(g, v, rho, p.MaxRpm, EngineQ);
                _o.WriteLine($"{id,-22} {area / disc,15:F4} | {pitch,6:F2} | {v,7:F1} | {stopped,9:F1} {stopped / q,5:F3} | " +
                             (w.Turning ? $"{w.Rpm,15:F0} {w.DragN,6:F1} {w.DragN / q,5:F3} | {stopped / Math.Max(1e-9, w.DragN),5:F2}"
                                        : $"{"STOPPED",15}                | {"-",5}"));
            }
        }
    }

    /// <summary>ITEM 4, the blade angle's own share: what a STOPPED propeller drags at each of its stops, so the
    /// coarse-pitch rule can be seen rather than asserted, and against what it drags WINDMILLING at the same stop.</summary>
    [Fact]
    public void StoppedDragAgainstBladeAngle()
    {
        if (Environment.GetEnvironmentVariable("PROPPHYS") != "1") return;
        double rho = Atmosphere.SeaLevelDensityKgM3;
        _o.WriteLine("type                    pitch deg |   V m/s | stopped N    Cd | windmilling rpm      N    Cd | stopped/windmilling");
        foreach (string id in new[] { "pitts-s2b-like", "extra-300-like", "great-lakes-like", "j3-cub-like" })
        {
            AircraftConfig c = Load(id);
            PropulsionConfig p = c.Propulsion!;
            double disc = Math.PI * 0.25 * p.PropDiameterM * p.PropDiameterM;
            double[] pitches = p.ConstantSpeed ? new[] { p.FineStopDeg, p.CoarseStopDeg } : new[] { p.BladePitch75Deg };
            foreach (double pitch in pitches)
            {
                if (pitch <= 0.0) continue;
                var g = new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, pitch, p.ActivityFactor);
                double EngineQ(double r) => PistonEngine.MotoringTorqueNm(p, r, 0.0, 1.0, 1.0);
                foreach (double v in new[] { 40.0, 60.0, 80.0 })
                {
                    double q = 0.5 * rho * v * v * disc;
                    double stopped = BladeElement.StoppedDragN(g, v, rho);
                    var w = BladeElement.Windmill(g, v, rho, p.MaxRpm, EngineQ);
                    _o.WriteLine($"{id,-22} {pitch,10:F2} | {v,7:F1} | {stopped,9:F1} {stopped / q,5:F3} | " +
                                 (w.Turning ? $"{w.Rpm,15:F0} {w.DragN,6:F1} {w.DragN / q,5:F3} | {stopped / Math.Max(1e-9, w.DragN),10:F2}"
                                            : $"{"STOPPED",15}                | {"-",10}"));
                }
            }
        }
    }
}

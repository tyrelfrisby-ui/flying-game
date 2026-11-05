using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Diagnostic (Ty 2026-11-05: "some sort of hammering noise ... no idea what it represents"): every sound event
/// the J-3 on floats raises on a water run, frame by frame at 60 fps (the rate FlightAudio samples GroundSoundMix).</summary>
[Collection("WorldTerrainActive")]
public sealed class FloatsHammerProbe
{
    private readonly ITestOutputHelper _o;
    public FloatsHammerProbe(ITestOutputHelper o) { _o = o; }
    private static double Pitch(RigidBodyState s) { Quat q = s.Attitude; return Math.Asin(Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1)) * 57.2958; }

    [Theory]
    [InlineData(0.3, 0.0)]
    [InlineData(1.0, -0.5)]
    public void WaterRunEvents(double power, double stick)
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "j3-cub-floats-like.json"));
            var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -(cfg.Floats!.KeelZ - 0.25)), new Quat(0, 0, 0, 1), Vec3.Zero, Vec3.Zero), ControlDeflections.Neutral);
            var sim = new SimLoop(ac);
            sim.RunFor(8, new ControlInputs(0, 0, 0, 1.0));
            var relay = new CrashRelay(ac);
            int hard = 0, impacts = 0, contacts = 0; double t = 0;
            var lines = new List<string>();
            ac.HardImpact += (c, p) => { hard++; if (lines.Count < 40) lines.Add($"  t={t:F2} HardImpact {c:F2} m/s at {p} water={ac.LastImpact.OnWater}"); };
            ac.Contact += e => { contacts++; if (lines.Count < 40) lines.Add($"  t={t:F2} Contact {e.Kind} {e.Part} {e.Intensity:F1}"); };
            Action<CrashImpact> onImp = i => { impacts++; if (lines.Count < 40) lines.Add($"  t={t:F2} CrashImpact {i.Surface} {i.NormalSpeedMs:F2} m/s sev {i.Severity01:F2}"); };
            CrashEvents.Impact += onImp;
            var mix = new GroundSoundMix(ac.Wheels.Count);
            double prevSplash = 0; int splashJumps = 0; double maxThump = 0;
            for (; t < 30; t += 1.0 / 60)
            {
                sim.RunFor(1.0 / 60, new ControlInputs(0, stick, 0, 1 - 2 * power));
                mix.Update(ac.Wheels, ac.MassProperties.MassKg * 9.80665, 1.0 / 60, FloatHydro.Floats, 2);
                if (Math.Abs(mix.WaterSplash - prevSplash) > 0.15) splashJumps++;
                prevSplash = mix.WaterSplash;
                maxThump = Math.Max(maxThump, Math.Max(mix.ThumpJ[0], mix.ThumpJ[1]));
                if ((int)Math.Round(t * 60) % 60 == 0)
                    _o.WriteLine($"t={t,4:F0} V={ac.State.Velocity.Length * 2.237,5:F1}mph pitch={Pitch(ac.State),5:F1} splash={mix.WaterSplash:F2} hiss={mix.WaterHiss:F2} spray={mix.Spray:F2} keel={mix.KeelScrape:F2} roll={mix.RollingGain.Sum():F2} thump={mix.ThumpJ[0] + mix.ThumpJ[1]:F1} hard={hard} impacts={impacts} contacts={contacts}");
            }
            CrashEvents.Impact -= onImp; relay.Detach();
            foreach (string l in lines) _o.WriteLine(l);
            _o.WriteLine($"power {power}: HardImpact {hard}, CrashImpact {impacts}, contact events {contacts}, splash jumps >0.15 {splashJumps}, max thump {maxThump:F1} J");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }
}

using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// USB rudder pedals with no pedals on the bench: simulated HID devices (name, VID/PID, axis layout, raw values)
/// through detection, mapping, invert, dead zone, the guided calibration, the toe-brake split, hot-plug and the
/// fallback to the controllers. Expected values are literals (CLAUDE.md, "A test that reads the file it is checking
/// proves nothing").
/// </summary>
public sealed class RudderPedalTests
{
    private sealed class Store : IPedalStore
    {
        public readonly Dictionary<string, string> Data = new();
        public string? Load(string key) => Data.TryGetValue(key, out var v) ? v : null;
        public void Save(string key, string value) => Data[key] = value;
    }

    private static PedalDevice Saitek() => new()
    {
        Id = "android:7", Name = "Saitek Pro Flight Rudder Pedals", VendorId = 0x06A3, ProductId = 0x0763, Backend = "Android",
        AxisNames = new[] { "x", "y", "rz" }, Axes = new[] { -1.0, -1.0, 0.0 },
    };

    private static PedalDevice GenericPedals() => new()
    {
        Id = "hid:3", Name = "USB Device", VendorId = 0x1234, ProductId = 0x5678, Backend = "HID",
        AxisNames = new[] { "x", "y", "rz" }, Axes = new[] { -1.0, -1.0, 0.0 },
    };

    private static PedalDevice Hotas() => new()
    {
        Id = "android:8", Name = "Thrustmaster T.Flight Hotas X", VendorId = 0x044F, ProductId = 0xB108, Backend = "Android",
        AxisNames = new[] { "x", "y", "z", "rz", "hatx", "haty" }, ButtonCount = 12, Axes = new double[6],
    };

    private static PedalDevice Gamepad() => new()
    {
        Id = "android:9", Name = "Xbox Wireless Controller", VendorId = 0x045E, ProductId = 0x0B13, Backend = "Android", IsGamepad = true,
        AxisNames = new[] { "x", "y", "z", "rz", "hatx", "haty", "brake", "gas" }, ButtonCount = 15, Axes = new double[8],
    };

    // ---- detection ----

    [Fact]
    public void KnownModelsAreTakenForPedals()
    {
        PedalMatch m = PedalCatalog.Classify(Saitek());
        Assert.True(m.IsPedals);
        Assert.Equal("Saitek / Logitech Pro Flight Rudder Pedals", m.Model);
        var tfrp = new PedalDevice { Name = "T.Flight Rudder Pedals", VendorId = 0x044F, ProductId = 0xB679, AxisNames = new[] { "x", "y", "rz" } };
        Assert.Equal("Thrustmaster T.Flight Rudder Pedals (TFRP)", PedalCatalog.Classify(tfrp).Model);
    }

    [Theory]
    [InlineData("VKB-Sim T-Rudder Mk.IV")]
    [InlineData("MFG Crosswind V3")]
    [InlineData("Thrustmaster T-Rudder")]
    [InlineData("Logitech G Pro Flight Rudder Pedals")]
    public void AnUnknownIdWithAPedalNameIsPedals(string name)
    {
        // VID/PID nobody has listed, and axes that say nothing (the Mac's HID shim names none).
        var d = new PedalDevice { Name = name, VendorId = 0x7777, ProductId = 0x0001, AxisNames = new[] { "axis 1", "axis 2", "axis 3" } };
        Assert.True(PedalCatalog.Classify(d).IsPedals);
    }

    [Fact]
    public void ThePedalLayoutAloneIsEnough()
    {
        // A generic name and an unknown id: a rudder axis, two toe axes, no buttons, no hat.
        PedalMatch m = PedalCatalog.Classify(GenericPedals());
        Assert.True(m.IsPedals, m.Reason);
        // Android's own simulation axes count too.
        var sim = new PedalDevice { Name = "Generic USB", AxisNames = new[] { "rudder", "brake", "gas" } };
        Assert.True(PedalCatalog.Classify(sim).IsPedals);
    }

    [Fact]
    public void SticksAndGamepadsAreNotPedals()
    {
        Assert.False(PedalCatalog.Classify(Hotas()).IsPedals);
        Assert.False(PedalCatalog.Classify(Gamepad()).IsPedals);
        // Even one called "rudder" is not taken if the platform says it is a gamepad.
        var g = Gamepad(); g.Name = "Rudder Gamepad";
        Assert.False(PedalCatalog.Classify(g).IsPedals);
        // A twist stick with a throttle and no hat (Android does not say how many buttons it has).
        var twist = new PedalDevice { Name = "USB Joystick", AxisNames = new[] { "x", "y", "rz", "throttle" } };
        Assert.False(PedalCatalog.Classify(twist).IsPedals);
    }

    // ---- default mapping ----

    [Fact]
    public void DefaultMappingByAxisName()
    {
        PedalProfile p = PedalCatalog.DefaultFor(GenericPedals());
        Assert.Equal(2, p.Rudder.Axis);
        Assert.Equal(0, p.LeftBrake.Axis);
        Assert.Equal(1, p.RightBrake.Axis);
        Assert.False(p.Rudder.Calibrated);
    }

    [Fact]
    public void AndroidSimulationAxesMapAndUseTheirZeroToOneRange()
    {
        var d = new PedalDevice { Name = "Pedals", AxisNames = new[] { "brake", "gas", "rudder" } };
        PedalProfile p = PedalCatalog.DefaultFor(d);
        Assert.Equal(2, p.Rudder.Axis);
        Assert.Equal(0, p.LeftBrake.Axis);
        Assert.Equal(1, p.RightBrake.Axis);
        Assert.Equal(0.0, p.LeftBrake.Min);
        Assert.Equal(1.0, p.LeftBrake.Max);
        Assert.Equal(0.0, p.LeftBrake.OneSided(0.0), 9);   // Android: released brake reads 0, not half way
        Assert.Equal(1.0, p.LeftBrake.OneSided(1.0), 9);
    }

    [Fact]
    public void UnnamedAxesMapByHidOrder()
    {
        var d = new PedalDevice { Name = "Rudder Pedals", AxisNames = new[] { "axis 1", "axis 2", "axis 3" } };
        PedalProfile p = PedalCatalog.DefaultFor(d);
        Assert.Equal((2, 0, 1), (p.Rudder.Axis, p.LeftBrake.Axis, p.RightBrake.Axis));
        var bar = new PedalDevice { Name = "Rudder bar", AxisNames = new[] { "axis 1" } };
        PedalProfile q = PedalCatalog.DefaultFor(bar);
        Assert.Equal((0, -1, -1), (q.Rudder.Axis, q.LeftBrake.Axis, q.RightBrake.Axis));
    }

    [Fact]
    public void AndroidAxisCodesHaveTheirNames()
    {
        Assert.Equal("x", PedalCatalog.AndroidAxisName(0));
        Assert.Equal("rz", PedalCatalog.AndroidAxisName(14));
        Assert.Equal("rudder", PedalCatalog.AndroidAxisName(20));
        Assert.Equal("gas", PedalCatalog.AndroidAxisName(22));
        Assert.Equal("brake", PedalCatalog.AndroidAxisName(23));
        Assert.Equal("generic1", PedalCatalog.AndroidAxisName(32));
    }

    // ---- one axis: centre, ends, invert, dead zone ----

    [Fact]
    public void RudderSignEndsAndInvert()
    {
        var c = new PedalAxisCal { Axis = 0, Min = -1, Max = 1, Center = 0, Dead = 0 };
        Assert.Equal(0.0, c.Centred(0.0), 9);
        Assert.Equal(1.0, c.Centred(1.0), 9);     // right pedal forward
        Assert.Equal(-1.0, c.Centred(-1.0), 9);
        Assert.Equal(0.5, c.Centred(0.5), 9);
        Assert.Equal(1.0, c.Centred(3.0), 9);     // past the end: clamped
        c.Invert = true;
        Assert.Equal(-1.0, c.Centred(1.0), 9);
        Assert.Equal(-0.5, c.Centred(0.5), 9);
    }

    [Fact]
    public void RudderOffCentreCalibrationReachesBothEnds()
    {
        // A worn pot: rests at 0.1, ends at −0.8 and +0.9. Each half scales to its own end.
        var c = new PedalAxisCal { Axis = 0, Min = -0.8, Max = 0.9, Center = 0.1, Dead = 0 };
        Assert.Equal(0.0, c.Centred(0.1), 9);
        Assert.Equal(1.0, c.Centred(0.9), 9);
        Assert.Equal(-1.0, c.Centred(-0.8), 9);
        Assert.Equal(0.5, c.Centred(0.5), 9);
        Assert.Equal(-0.5, c.Centred(-0.35), 9);
        c.Invert = true;   // mirrored about the same centre: rest still reads 0
        Assert.Equal(0.0, c.Centred(0.1), 9);
        Assert.Equal(-1.0, c.Centred(0.9), 9);
        Assert.Equal(1.0, c.Centred(-0.8), 9);
    }

    [Fact]
    public void RudderDeadZoneIsRescaled()
    {
        var c = new PedalAxisCal { Axis = 0, Min = -1, Max = 1, Center = 0, Dead = 0.1 };
        Assert.Equal(0.0, c.Centred(0.09), 9);
        Assert.Equal(0.0, c.Centred(-0.1), 9);
        Assert.Equal(0.4 / 0.9, c.Centred(0.5), 9);
        Assert.Equal(1.0, c.Centred(1.0), 9);     // full is still full
        Assert.Equal(-1.0, c.Centred(-1.0), 9);
    }

    [Fact]
    public void ToeBrakeRangeInvertAndDeadZone()
    {
        var c = new PedalAxisCal { Axis = 0, Min = -1, Max = 1, Dead = 0 };
        Assert.Equal(0.0, c.OneSided(-1.0), 9);   // released at the bottom of a −1…1 HID axis
        Assert.Equal(0.5, c.OneSided(0.0), 9);
        Assert.Equal(1.0, c.OneSided(1.0), 9);
        Assert.Equal(0.0, c.OneSided(-2.0), 9);
        c.Invert = true;                           // rests at the top
        Assert.Equal(0.0, c.OneSided(1.0), 9);
        Assert.Equal(1.0, c.OneSided(-1.0), 9);
        c.Invert = false; c.Dead = 0.05;
        Assert.Equal(0.0, c.OneSided(-0.92), 9);  // 4 % in: still released
        Assert.Equal(0.45 / 0.95, c.OneSided(0.0), 9);
        Assert.Equal(1.0, c.OneSided(1.0), 9);
    }

    [Fact]
    public void MapperReadsTheAssignedAxesAndIgnoresMissingOnes()
    {
        var p = new PedalProfile();
        p.Rudder.Axis = 2; p.Rudder.Dead = 0;
        p.LeftBrake.Axis = 0; p.LeftBrake.Dead = 0;
        p.RightBrake.Axis = 7; p.RightBrake.Dead = 0;   // not on this device
        PedalFrame f = PedalMapper.Read(p, new[] { 1.0, -1.0, -0.25 });
        Assert.True(f.Active);
        Assert.Equal(-0.25, f.Rudder, 9);
        Assert.Equal(1.0, f.LeftBrake, 9);
        Assert.Equal(0.0, f.RightBrake, 9);
    }

    // ---- persistence ----

    [Fact]
    public void ProfileRoundTripsInAnyCulture()
    {
        var p = new PedalProfile { Enabled = false };
        p.Rudder.Axis = 2; p.Rudder.Invert = true; p.Rudder.Dead = 0.07; p.Rudder.Min = -0.83; p.Rudder.Max = 0.91; p.Rudder.Center = 0.04; p.Rudder.Calibrated = true;
        p.LeftBrake.Axis = 0; p.LeftBrake.Min = -1; p.LeftBrake.Max = 0.97;
        p.RightBrake.Axis = -1;
        CultureInfo was = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");   // a decimal comma must not leak in
            string s = p.Serialize();
            Assert.DoesNotContain("0,07", s);
            PedalProfile? q = PedalProfile.Parse(s);
            Assert.NotNull(q);
            Assert.False(q!.Enabled);
            Assert.Equal(2, q.Rudder.Axis);
            Assert.True(q.Rudder.Invert);
            Assert.Equal(0.07, q.Rudder.Dead);
            Assert.Equal(-0.83, q.Rudder.Min);
            Assert.Equal(0.91, q.Rudder.Max);
            Assert.Equal(0.04, q.Rudder.Center);
            Assert.True(q.Rudder.Calibrated);
            Assert.Equal(0.97, q.LeftBrake.Max);
            Assert.Equal(-1, q.RightBrake.Axis);
        }
        finally { Thread.CurrentThread.CurrentCulture = was; }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("pedals1|on=1|rudder=2,0,0.03,-1,1,0,0|left=0,0,0.05,-1,1,0,0")]           // a role missing
    [InlineData("pedals1|on=1|rudder=2,0,x,-1,1,0,0|left=0,0,0.05,-1,1,0,0|right=1,0,0.05,-1,1,0,0")]
    [InlineData("pedals0|on=1|rudder=2,0,0.03,-1,1,0,0|left=0,0,0.05,-1,1,0,0|right=1,0,0.05,-1,1,0,0")]
    public void ACorruptProfileIsIgnored(string? s) => Assert.Null(PedalProfile.Parse(s));

    // ---- the toe brakes into the sim ----

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 0.0)]
    [InlineData(0.0, 1.0)]
    [InlineData(0.3, 0.8)]
    [InlineData(1.0, 1.0)]
    [InlineData(0.6, 0.6)]
    public void ToeBrakesComeBackOutOfTheSimExactly(double left, double right)
    {
        // The sim brakes the left wheel brake × (1 − bias) and the right brake × (1 + bias) (LandingGear.Compute).
        (double brake, double bias) = PedalBlend.ToSim(left, right);
        Assert.InRange(brake, 0.0, 1.0);
        Assert.InRange(bias, -1.0, 1.0);
        Assert.Equal(left, System.Math.Clamp(brake * (1.0 - bias), 0.0, 1.0), 9);
        Assert.Equal(right, System.Math.Clamp(brake * (1.0 + bias), 0.0, 1.0), 9);
    }

    [Fact]
    public void LeftToeAloneBrakesOnlyTheLeftWheel()
    {
        (double brake, double bias) = PedalBlend.ToSim(1.0, 0.0);
        Assert.Equal(0.5, brake, 9);
        Assert.Equal(-1.0, bias, 9);   // −1 = left only
    }

    [Fact]
    public void EachWheelGetsTheHarderOfHandAndToe()
    {
        // Hand: 0.4 on both (no rudder bias). Toes: right to the floor.
        var p = new PedalFrame { Active = true, RightBrake = 1.0 };
        (double brake, double bias) = PedalBlend.Combine(0.4, 0.0, p);
        (double l, double r) = PedalBlend.FromSim(brake, bias);
        Assert.Equal(0.4, l, 9);
        Assert.Equal(1.0, r, 9);
    }

    [Fact]
    public void WithoutPedalsTheHandIsUntouched()
    {
        var none = default(PedalFrame);
        Assert.Equal((0.7, -0.3), PedalBlend.Combine(0.7, -0.3, none));
        Assert.Equal(0.42, PedalBlend.Rudder(0.42, none));
    }

    [Fact]
    public void RudderFromPedalsAndHandAddsAndClamps()
    {
        var p = new PedalFrame { Active = true, Rudder = 0.6 };
        Assert.Equal(0.6, PedalBlend.Rudder(0.0, p), 9);
        Assert.Equal(0.3, PedalBlend.Rudder(-0.3, p), 9);
        Assert.Equal(1.0, PedalBlend.Rudder(0.7, p), 9);
    }

    // ---- the session: hot-plug, fallback, choice, persistence, logging ----

    private static (PedalSession s, Store st, List<string> log) NewSession()
    {
        var st = new Store();
        var log = new List<string>();
        return (new PedalSession(st, log.Add), st, log);
    }

    [Fact]
    public void NoPedalsMeansTheControllersFly()
    {
        var (s, _, _) = NewSession();
        s.Update(new List<PedalDevice>(), 1 / 72.0);
        Assert.False(s.Active);
        Assert.False(s.Connected);
        // Only a gamepad and a stick: still no pedals.
        s.Update(new List<PedalDevice> { Gamepad(), Hotas() }, 1 / 72.0);
        Assert.False(s.Active);
        Assert.Equal("none detected (2 other controllers)", s.DeviceLine());
    }

    [Fact]
    public void HotPlugTakesOverAndHandsBack()
    {
        var (s, _, log) = NewSession();
        var devs = new List<PedalDevice> { Hotas() };
        s.Update(devs, 1 / 72.0);
        Assert.False(s.Active);

        PedalDevice ped = Saitek();
        devs = new List<PedalDevice> { devs[0], ped };
        ped.Axes[2] = 0.5;   // half right rudder
        s.Update(devs, 1 / 72.0);
        Assert.True(s.Active);
        Assert.Same(ped, s.Device);
        Assert.InRange(s.Frame.Rudder, 0.48, 0.49);    // 0.5 less the 3 % dead zone, rescaled = 0.4845
        Assert.Equal(0.0, s.Frame.LeftBrake, 9);       // x and y rest at −1: released
        Assert.Contains(log, l => l.StartsWith("[Pedals] connected: \"Saitek Pro Flight Rudder Pedals\" 06A3:0763 via Android, 3 axes [x y rz]") && l.Contains("PEDALS"));
        Assert.Contains(log, l => l.StartsWith("[Pedals] connected: \"Thrustmaster T.Flight Hotas X\"") && l.Contains("not pedals"));
        Assert.Contains(log, l => l.StartsWith("[Pedals] using \"Saitek Pro Flight Rudder Pedals\"") && l.Contains("rudder RZ, left brake X, right brake Y"));

        // Unplugged: back on the controllers at once, and it says so.
        s.Update(new List<PedalDevice> { devs[0] }, 1 / 72.0);
        Assert.False(s.Active);
        Assert.Null(s.Device);
        Assert.Contains(log, l => l.StartsWith("[Pedals] disconnected: \"Saitek Pro Flight Rudder Pedals\" 06A3:0763"));
        Assert.Contains(log, l => l == "[Pedals] no pedals now: rudder and brakes back on the controllers");
    }

    [Fact]
    public void NothingIsLoggedOrReselectedWhileTheDevicesStayTheSame()
    {
        var (s, _, log) = NewSession();
        var devs = new List<PedalDevice> { Saitek() };
        s.Update(devs, 1 / 72.0);
        int n = log.Count;
        for (int i = 0; i < 100; i++) s.Update(devs, 1 / 72.0);
        Assert.Equal(n, log.Count);
    }

    [Fact]
    public void ASettingSurvivesAReplug()
    {
        var (s, st, _) = NewSession();
        s.Update(new List<PedalDevice> { Saitek() }, 1 / 72.0);
        s.ToggleInvert(PedalRole.Rudder);
        s.AdjustDead(PedalRole.LeftBrake, 0.05);
        s.Update(new List<PedalDevice>(), 1 / 72.0);
        Assert.True(st.Data.ContainsKey("pedals.profile.06A3:0763"));

        // A NEW device object (another port, another launch) with the same ids: the profile comes back.
        PedalDevice again = Saitek();
        again.Id = "android:12";
        again.Axes[2] = 1.0;
        s.Update(new List<PedalDevice> { again }, 1 / 72.0);
        Assert.True(s.Profile.Rudder.Invert);
        Assert.Equal(0.10, s.Profile.LeftBrake.Dead, 9);
        Assert.Equal(-1.0, s.Frame.Rudder, 9);

        // A brand-new session (the next launch) reads the same store.
        var s2 = new PedalSession(st);
        s2.Update(new List<PedalDevice> { Saitek() }, 1 / 72.0);
        Assert.True(s2.Profile.Rudder.Invert);
    }

    [Fact]
    public void SwitchedOffPedalsLeaveTheControllersAlone()
    {
        var (s, _, _) = NewSession();
        var devs = new List<PedalDevice> { Saitek() };
        s.Update(devs, 1 / 72.0);
        s.SetEnabled(false);
        devs[0].Axes[2] = 1.0;
        s.Update(devs, 1 / 72.0);
        Assert.True(s.Connected);
        Assert.False(s.Active);
        Assert.Equal(0.3, PedalBlend.Rudder(0.3, s.Frame));
    }

    [Fact]
    public void ThePilotCanPickADeviceDetectionMissed()
    {
        // A home-built pedal box that calls itself a joystick and has buttons: not detected …
        var odd = new PedalDevice { Id = "hid:5", Name = "Arduino Leonardo", VendorId = 0x2341, ProductId = 0x8036, Backend = "HID",
            AxisNames = new[] { "x", "y", "z", "rx" }, ButtonCount = 8, Axes = new double[4] };
        var (s, st, _) = NewSession();
        var devs = new List<PedalDevice> { odd, Gamepad() };
        s.Update(devs, 1 / 72.0);
        Assert.False(s.Connected);
        // … so he picks it. Gamepads are not offered.
        s.CycleDevice(1);
        Assert.Same(odd, s.Device);
        Assert.Equal("2341:8036", st.Data[PedalSession.ChoiceKey]);
        Assert.Equal(2, s.Profile.Rudder.Axis);   // z: the named default
        s.CycleDevice(1);                          // back to detect
        Assert.False(s.Connected);
        Assert.Equal("", st.Data[PedalSession.ChoiceKey]);
    }

    [Fact]
    public void CyclingAnAxisWrapsThroughNone()
    {
        var (s, _, _) = NewSession();
        s.Update(new List<PedalDevice> { Saitek() }, 1 / 72.0);
        Assert.Equal(2, s.Profile.Rudder.Axis);
        s.CycleAxis(PedalRole.Rudder, 1);
        Assert.Equal(-1, s.Profile.Rudder.Axis);
        s.CycleAxis(PedalRole.Rudder, 1);
        Assert.Equal(0, s.Profile.Rudder.Axis);
        s.CycleAxis(PedalRole.Rudder, -1);
        s.CycleAxis(PedalRole.Rudder, -1);
        Assert.Equal(2, s.Profile.Rudder.Axis);
        Assert.Equal("none", new PedalSession(new Store()).AxisName(new PedalAxisCal()));
    }

    // ---- the guided calibration, from simulated pedals ----

    private static void Run(PedalSession s, List<PedalDevice> devs, double seconds, double rud, double left, double right)
    {
        PedalDevice d = devs[0];
        for (double t = 0; t < seconds; t += 1 / 72.0)
        {
            d.Axes[0] = left; d.Axes[1] = right; d.Axes[2] = rud;
            s.Update(devs, 1 / 72.0);
        }
    }

    [Fact]
    public void CalibrationFindsTheAxesTheirDirectionsAndTheirEnds()
    {
        // An awkward pair: axes in an unusual order (rudder on axis 0, left toe on 2, right toe on 1), the rudder
        // running BACKWARDS (right pedal = lower value), the toes resting at the TOP and reading lower when pressed,
        // and none of them reaching the nominal ends.
        var dev = new PedalDevice { Id = "hid:1", Name = "Rudder Pedals", Backend = "HID", AxisNames = new[] { "axis 1", "axis 2", "axis 3" }, Axes = new double[3] };
        var devs = new List<PedalDevice> { dev };
        var (s, _, log) = NewSession();
        void Set(double rud, double l, double r) { dev.Axes[0] = rud; dev.Axes[2] = l; dev.Axes[1] = r; s.Update(devs, 1 / 72.0); }
        void Hold(double sec, double rud, double l, double r) { for (double t = 0; t < sec; t += 1 / 72.0) Set(rud, l, r); }

        Hold(0.1, 0.05, 0.9, 0.88);
        s.StartCalibration();
        Assert.False(s.Active);                       // nothing flies from the pedals while they are learnt
        Hold(1.2, 0.05, 0.9, 0.88);                   // rest
        Assert.Equal(PedalCalibrator.Step.RudderRight, s.Calibration!.Current);
        Hold(0.6, -0.85, 0.9, 0.88);                  // right pedal forward: axis 0 goes DOWN
        Assert.Equal(PedalCalibrator.Step.LeftToe, s.Calibration.Current);
        Hold(0.3, 0.05, 0.9, 0.88);                   // centre again
        Hold(0.6, 0.05, -0.92, 0.88);                 // left toe: axis 2 goes down
        Assert.Equal(PedalCalibrator.Step.RightToe, s.Calibration.Current);
        Hold(0.3, 0.05, 0.9, 0.88);
        Hold(0.6, 0.05, 0.9, -0.9);                   // right toe: axis 1
        Assert.Equal(PedalCalibrator.Step.Sweep, s.Calibration.Current);
        Hold(0.3, 0.95, 0.9, 0.88);                   // full LEFT rudder during the sweep
        Hold(0.3, 0.05, 0.9, 0.88);
        s.FinishCalibration();

        Assert.Null(s.Calibration);
        PedalProfile p = s.Profile;
        Assert.Equal((0, 2, 1), (p.Rudder.Axis, p.LeftBrake.Axis, p.RightBrake.Axis));
        Assert.True(p.Rudder.Invert);
        Assert.True(p.LeftBrake.Invert);
        Assert.True(p.RightBrake.Invert);
        Assert.Equal(-0.85, p.Rudder.Min, 9);
        Assert.Equal(0.95, p.Rudder.Max, 9);
        Assert.Equal(0.05, p.Rudder.Center, 3);
        Assert.True(p.Rudder.Calibrated && p.LeftBrake.Calibrated && p.RightBrake.Calibrated);
        Assert.Contains(log, l => l.StartsWith("[Pedals] calibrated: rudder AXIS 1 inverted, left brake AXIS 3 inverted, right brake AXIS 2 inverted"));

        // And now it flies right: right pedal = +1, rest = 0, each toe to the floor = 1 on its own wheel.
        Set(-0.85, 0.9, 0.88);
        Assert.Equal(1.0, s.Frame.Rudder, 6);
        Set(0.95, 0.9, 0.88);
        Assert.Equal(-1.0, s.Frame.Rudder, 6);
        Set(0.05, 0.9, 0.88);
        Assert.Equal(0.0, s.Frame.Rudder, 6);
        Assert.Equal(0.0, s.Frame.LeftBrake, 6);
        Assert.Equal(0.0, s.Frame.RightBrake, 6);
        Set(0.05, -0.92, 0.88);
        Assert.Equal(1.0, s.Frame.LeftBrake, 6);
        Assert.Equal(0.0, s.Frame.RightBrake, 6);
        (double brake, double bias) = PedalBlend.ToSim(s.Frame.LeftBrake, s.Frame.RightBrake);
        Assert.Equal(-1.0, bias, 9);                  // the left wheel only
        Assert.Equal(0.5, brake, 9);
    }

    [Fact]
    public void SkippedStepsKeepTheirAxesAndAnUnmovedAxisKeepsItsRange()
    {
        var (s, _, _) = NewSession();
        var devs = new List<PedalDevice> { Saitek() };
        Run(s, devs, 0.1, 0, -1, -1);
        s.StartCalibration();
        Run(s, devs, 1.2, 0, -1, -1);
        s.SkipCalibrationStep();   // rudder
        s.SkipCalibrationStep();   // left toe
        s.SkipCalibrationStep();   // right toe
        Assert.Equal(PedalCalibrator.Step.Sweep, s.Calibration!.Current);
        Run(s, devs, 0.5, 0.8, -1, -1);
        Run(s, devs, 0.5, -0.7, -1, -1);
        s.FinishCalibration();
        Assert.Equal((2, 0, 1), (s.Profile.Rudder.Axis, s.Profile.LeftBrake.Axis, s.Profile.RightBrake.Axis));
        Assert.Equal(-0.7, s.Profile.Rudder.Min, 9);
        Assert.Equal(0.8, s.Profile.Rudder.Max, 9);
        Assert.False(s.Profile.LeftBrake.Calibrated);   // never pressed: still its nominal −1…1
        Assert.Equal(-1.0, s.Profile.LeftBrake.Min);
    }

    [Fact]
    public void CancellingTheCalibrationChangesNothing()
    {
        var (s, _, _) = NewSession();
        var devs = new List<PedalDevice> { Saitek() };
        Run(s, devs, 0.1, 0, -1, -1);
        string before = s.Profile.Serialize();
        s.StartCalibration();
        Run(s, devs, 1.2, 0, -1, -1);
        Run(s, devs, 0.6, 0.9, -1, -1);
        s.CancelCalibration();
        Assert.Equal(before, s.Profile.Serialize());
        Run(s, devs, 0.1, 0.5, -1, -1);
        Assert.True(s.Active);
    }

    [Fact]
    public void UnpluggedMidCalibrationStopsIt()
    {
        var (s, _, _) = NewSession();
        var devs = new List<PedalDevice> { Saitek() };
        Run(s, devs, 0.1, 0, -1, -1);
        s.StartCalibration();
        s.Update(new List<PedalDevice>(), 1 / 72.0);
        Assert.Null(s.Calibration);
        Assert.False(s.Active);
    }

    // ---- text for the menus ----

    [Fact]
    public void BarsAndLines()
    {
        Assert.Equal("[-----|-----]", PedalSession.Bar(0.0, true));
        Assert.Equal("[-----|#####]", PedalSession.Bar(1.0, true));
        Assert.Equal("[--###|-----]", PedalSession.Bar(-0.6, true));   // filled out from the centre
        Assert.Equal("[#####-----]", PedalSession.Bar(0.5, false));
        var (s, _, _) = NewSession();
        var devs = new List<PedalDevice> { Saitek() };
        Run(s, devs, 0.1, 1.0, 1.0, -1);
        Assert.Equal("Saitek Pro Flight Rudder Pedals  06A3:0763", s.DeviceLine());
        Assert.Equal("RZ  dz 3%  [-----|#####] +100  (uncal.)", s.RoleLine(PedalRole.Rudder));
        Assert.Equal("X  dz 5%  [##########] 100%  (uncal.)", s.RoleLine(PedalRole.LeftBrake));
        Assert.Equal("RUD +100  L 100%  R 0%", s.LiveLine());
        Assert.Equal("RZ [----|####] +100", s.RoleCompact(PedalRole.Rudder));
        Assert.Equal("Y [--------] 0%", s.RoleCompact(PedalRole.RightBrake));
        s.ToggleInvert(PedalRole.LeftBrake);
        Assert.Equal("X inv [--------] 0%", s.RoleCompact(PedalRole.LeftBrake));
    }
}

using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FlyingGame.Core;

/// <summary>
/// USB RUDDER PEDALS (owner Ty, 2026-11-02, item 11: "usb rudder pedal support" — Quest over USB-C OTG, and iOS /
/// Mac where they can). Everything here is engine-free: the Bridge (Bridge/RudderPedals.cs) only lists the game
/// controllers it can see and copies each one's raw axes into a <see cref="PedalDevice"/> every frame; which device
/// is the pedals, which of its axes is the rudder and which the toe brakes, how each axis is calibrated, and how
/// the result joins the hand controls are all decided here and pinned by tools/FlightTests/RudderPedalTests.cs with
/// simulated HID input, because there is no pair of pedals on the bench.
///
/// ONE INPUT PATH. The pedals do not drive the aeroplane themselves: <see cref="PedalBlend"/> folds them into the
/// same rudder and brake values the touch pads, a joystick and the Touch controllers already feed
/// (TouchFlightControls.PublishToDriver), so the flight model, the cockpit's moving pedals (CockpitLive.PedalFwdDeg,
/// from the rudder) and the rig's toe brakes (CockpitRigState, from the sim's brake and bias) follow them with no
/// path of their own. The rudder ADDS to the hand's (each its own offset, the total clamped — CLAUDE.md, "A silent
/// exclusive mode is usually a bug in disguise"), and each toe brake is the larger of the pedal and the hand's.
///
/// DETECTION does not rely on a VID/PID table. A known model is a strong hint (<see cref="PedalCatalog.Known"/>),
/// a name that says "rudder" or "pedal" another, and an axis layout that looks like pedals (a rudder axis and two
/// toe axes, few or no buttons, no hat, not a gamepad) is enough on its own. Whatever is detected, the pilot can
/// pick the device and each axis by hand in Options → Controls (or the VR menu's Rudder pedals page) and the
/// guided calibration finds the axes by asking him to push each pedal.
/// </summary>
public enum PedalRole { Rudder = 0, LeftBrake = 1, RightBrake = 2 }

/// <summary>One game controller as the Bridge sees it. The Bridge keeps the same object while the device stays
/// connected (that is how a hot-plug is noticed) and overwrites <see cref="Axes"/> in place every frame.</summary>
public sealed class PedalDevice
{
    /// <summary>Unique while connected: the backend's own device id.</summary>
    public string Id = "";
    public string Name = "";
    /// <summary>USB vendor / product id; 0 = the platform does not say (the Mac's HID shim, Unity's old input).</summary>
    public int VendorId, ProductId;
    /// <summary>"Android", "HID", "Joystick", "MacHID", "Legacy": which reader produced it (for the log).</summary>
    public string Backend = "";
    /// <summary>Lower-case axis names where the platform has them ("x", "y", "rz", "rudder", "brake", "gas"),
    /// else "axis 1" …</summary>
    public string[] AxisNames = Array.Empty<string>();
    public int ButtonCount;
    /// <summary>The platform itself calls it a gamepad.</summary>
    public bool IsGamepad;
    /// <summary>Raw axis values, as the platform reports them (usually −1…1, or 0…1 for a brake / gas axis).</summary>
    public double[] Axes = Array.Empty<double>();

    private string? _key;
    /// <summary>What a profile is stored under: VID:PID when there is one (the same pedals on another port, or on
    /// another day, keep their calibration), else the name.</summary>
    public string Key => _key ??= VendorId != 0 || ProductId != 0 ? VidPid : "name:" + Name;
    public string VidPid => VendorId != 0 || ProductId != 0 ? VendorId.ToString("X4") + ":" + ProductId.ToString("X4") : "----:----";
    public bool NamedAxes => AxisNames.Length > 0 && !AxisNames[0].StartsWith("axis ", StringComparison.Ordinal);
}

/// <summary>How much a device looks like rudder pedals, and why.</summary>
public readonly struct PedalMatch
{
    public readonly int Score;
    public readonly string Reason;
    /// <summary>The known model, or null.</summary>
    public readonly string? Model;
    public PedalMatch(int score, string reason, string? model) { Score = score; Reason = reason; Model = model; }
    public bool IsPedals => Score >= PedalCatalog.PedalScore;
}

/// <summary>Known pedals, and the generic test for everything else.</summary>
public static class PedalCatalog
{
    /// <summary>A device scoring this much is taken for pedals without being asked.</summary>
    public const int PedalScore = 50;

    /// <summary>Models whose USB ids we are sure of, with their usual axis layout. A hint only: every other pair is
    /// found by name or by layout, and the guided calibration reassigns the axes whatever this says.</summary>
    public static readonly (int Vid, int Pid, string Model, string Rudder, string Left, string Right)[] Known =
    {
        (0x06A3, 0x0763, "Saitek / Logitech Pro Flight Rudder Pedals", "rz", "x", "y"),
        (0x044F, 0xB679, "Thrustmaster T.Flight Rudder Pedals (TFRP)", "rz", "x", "y"),
    };

    /// <summary>Makers of sim hardware, by USB vendor id (a weak hint: they make sticks too).</summary>
    public static string? Vendor(int vid) => vid switch
    {
        0x044F => "Thrustmaster",
        0x046D => "Logitech",
        0x06A3 => "Saitek",
        0x231D => "VKB",
        _ => null,
    };

    private static readonly string[] StrongWords = { "rudder", "pedal", "crosswind", "tfrp", "t-rudder", "pendular" };

    /// <summary>A name that says pedals ("Thrustmaster T-Rudder", "MFG Crosswind", "Pro Flight Rudder Pedals").</summary>
    public static bool NameSaysPedals(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        string n = name!.ToLowerInvariant();
        foreach (string w in StrongWords) if (n.Contains(w)) return true;
        return n.StartsWith("mfg", StringComparison.Ordinal);
    }

    private static bool Has(PedalDevice d, string axis) => Array.IndexOf(d.AxisNames, axis) >= 0;

    public static PedalMatch Classify(PedalDevice d)
    {
        int score = 0;
        string? model = null;
        var why = new StringBuilder();
        foreach (var k in Known)
            if (k.Vid == d.VendorId && k.Pid == d.ProductId) { score += 100; model = k.Model; why.Append("known model ").Append(k.Model); }
        if (NameSaysPedals(d.Name)) { score += 60; Sep(why).Append("name says pedals"); }
        if (d.NamedAxes)
        {
            bool rudder = Has(d, "rudder") || Has(d, "rz") || Has(d, "z");
            int toes = (Has(d, "x") ? 1 : 0) + (Has(d, "y") ? 1 : 0) + (Has(d, "brake") ? 1 : 0) + (Has(d, "gas") ? 1 : 0) + (Has(d, "accelerator") ? 1 : 0);
            bool hat = Has(d, "hatx") || Has(d, "haty") || Has(d, "hat");
            // Pedals have no throttle or slider; a twist stick with a throttle lever has x, y, rz AND one of them.
            bool lever = Has(d, "throttle") || Has(d, "slider");
            if (rudder && toes >= 2 && d.AxisNames.Length <= 4 && d.ButtonCount <= 2 && !hat && !lever && !d.IsGamepad)
            { score += 50; Sep(why).Append("pedal layout (rudder + two toe axes, no buttons)"); }
            else if (hat) { score -= 30; Sep(why).Append("has a hat"); }
            else if (lever) { score -= 30; Sep(why).Append("has a throttle (a stick?)"); }
        }
        if (d.IsGamepad) { score -= 100; Sep(why).Append("gamepad"); }
        else if (d.ButtonCount >= 6) { score -= 40; Sep(why).Append(d.ButtonCount).Append(" buttons (a stick?)"); }
        string? vendor = Vendor(d.VendorId);
        if (vendor != null) { score += 5; Sep(why).Append(vendor); }
        if (why.Length == 0) why.Append("nothing pedal-like");
        return new PedalMatch(score, why.ToString(), model);
    }

    private static StringBuilder Sep(StringBuilder b) => b.Length > 0 ? b.Append(", ") : b;

    /// <summary>The range an axis reports at rest-to-full, before any calibration. Android reports its brake, gas,
    /// throttle and trigger axes 0…1 and everything else −1…1; the HID readers report −1…1.</summary>
    public static (double Lo, double Hi) NominalRange(string axisName) => axisName switch
    {
        "brake" or "gas" or "accelerator" or "throttle" or "ltrigger" or "rtrigger" => (0.0, 1.0),
        _ => (-1.0, 1.0),
    };

    /// <summary>The mapping a device starts with: the known model's, else by axis name (rudder = rudder / rz / z;
    /// left toe = x / brake; right toe = y / gas), else by position (the HID order is X, Y, then Z / Rz: left toe,
    /// right toe, rudder). Uncalibrated: each axis's nominal range, a small dead zone.</summary>
    public static PedalProfile DefaultFor(PedalDevice d)
    {
        var p = new PedalProfile();
        int n = d.AxisNames.Length;
        int rud = -1, left = -1, right = -1;
        foreach (var k in Known)
            if (k.Vid == d.VendorId && k.Pid == d.ProductId)
            { rud = Array.IndexOf(d.AxisNames, k.Rudder); left = Array.IndexOf(d.AxisNames, k.Left); right = Array.IndexOf(d.AxisNames, k.Right); }
        if (rud < 0 && left < 0 && right < 0 && d.NamedAxes)
        {
            rud = First(d, -1, -1, "rudder", "rz", "z", "twist", "wheel");
            left = First(d, rud, -1, "x", "brake", "ltrigger");
            right = First(d, rud, left, "y", "gas", "accelerator", "rtrigger");
        }
        else if (rud < 0 && left < 0 && right < 0)
        {
            if (n >= 3) { left = 0; right = 1; rud = 2; }
            else if (n >= 1) rud = 0;
        }
        Assign(p.Rudder, d, rud);
        Assign(p.LeftBrake, d, left);
        Assign(p.RightBrake, d, right);
        return p;
    }

    private static int First(PedalDevice d, int notA, int notB, params string[] names)
    {
        foreach (string s in names)
        {
            int i = Array.IndexOf(d.AxisNames, s);
            if (i >= 0 && i != notA && i != notB) return i;
        }
        return -1;
    }

    private static void Assign(PedalAxisCal c, PedalDevice d, int axis)
    {
        c.Axis = axis;
        (c.Min, c.Max) = axis >= 0 && axis < d.AxisNames.Length ? NominalRange(d.AxisNames[axis]) : (-1.0, 1.0);
        c.Center = 0.5 * (c.Min + c.Max);
        c.Calibrated = false;
    }

    /// <summary>Android's MotionEvent axis codes (AXIS_X = 0 … AXIS_GENERIC_16 = 47) as the names used here.</summary>
    public static string AndroidAxisName(int code) => code switch
    {
        0 => "x", 1 => "y", 11 => "z", 12 => "rx", 13 => "ry", 14 => "rz", 15 => "hatx", 16 => "haty",
        17 => "ltrigger", 18 => "rtrigger", 19 => "throttle", 20 => "rudder", 21 => "wheel", 22 => "gas", 23 => "brake",
        >= 32 and <= 47 => "generic" + (code - 31).ToString(CultureInfo.InvariantCulture),
        _ => "axis" + code.ToString(CultureInfo.InvariantCulture),
    };
}

/// <summary>One axis's assignment and calibration. Raw values are whatever the platform reports; the calibration
/// says where its ends (and, for the rudder, its centre) really are.</summary>
public sealed class PedalAxisCal
{
    /// <summary>Index into the device's axes; −1 = not assigned.</summary>
    public int Axis = -1;
    public bool Invert;
    /// <summary>Dead zone as a fraction of travel (rescaled, so full deflection is still full).</summary>
    public double Dead;
    /// <summary>The raw values at the two ends (Min &lt; Max), and at rest for a centring axis.</summary>
    public double Min = -1.0, Max = 1.0, Center;
    /// <summary>The ends were measured (else they are the axis's nominal range).</summary>
    public bool Calibrated;

    public bool Set => Axis >= 0;

    public PedalAxisCal Clone() => (PedalAxisCal)MemberwiseClone();

    /// <summary>A centring axis (the rudder): −1…1, + = right pedal forward.</summary>
    public double Centred(double raw)
    {
        double lo = Math.Min(Min, Max), hi = Math.Max(Min, Max), c = Math.Clamp(Center, lo, hi), v = raw;
        if (Invert) { v = lo + hi - v; c = lo + hi - c; }
        double n = v >= c ? (hi - c > 1e-6 ? (v - c) / (hi - c) : 0.0) : (c - lo > 1e-6 ? (v - c) / (c - lo) : 0.0);
        n = Math.Clamp(n, -1.0, 1.0);
        double a = Math.Abs(n), dz = Math.Clamp(Dead, 0.0, 0.5);
        return a <= dz ? 0.0 : Math.Sign(n) * (a - dz) / (1.0 - dz);
    }

    /// <summary>A one-sided axis (a toe brake): 0 released … 1 fully pressed. Released is Min (Max when inverted).</summary>
    public double OneSided(double raw)
    {
        double lo = Math.Min(Min, Max), hi = Math.Max(Min, Max);
        double released = Invert ? hi : lo, pressed = Invert ? lo : hi;
        if (Math.Abs(pressed - released) < 1e-6) return 0.0;
        double t = Math.Clamp((raw - released) / (pressed - released), 0.0, 1.0), dz = Math.Clamp(Dead, 0.0, 0.5);
        return t <= dz ? 0.0 : (t - dz) / (1.0 - dz);
    }
}

/// <summary>What the pilot chose for one pair of pedals (stored per device, <see cref="PedalDevice.Key"/>).</summary>
public sealed class PedalProfile
{
    public const double RudderDead = 0.03, BrakeDead = 0.05;

    /// <summary>Off = the device is ignored and the controllers keep the rudder and brakes.</summary>
    public bool Enabled = true;
    public PedalAxisCal Rudder = new() { Dead = RudderDead };
    public PedalAxisCal LeftBrake = new() { Dead = BrakeDead };
    public PedalAxisCal RightBrake = new() { Dead = BrakeDead };

    public PedalAxisCal this[PedalRole r] => r switch { PedalRole.Rudder => Rudder, PedalRole.LeftBrake => LeftBrake, _ => RightBrake };

    public PedalProfile Clone() => new()
    {
        Enabled = Enabled, Rudder = Rudder.Clone(), LeftBrake = LeftBrake.Clone(), RightBrake = RightBrake.Clone(),
    };

    private const string Magic = "pedals1";

    /// <summary>One line, invariant culture: <c>pedals1|on=1|rudder=axis,invert,dead,min,max,center,calibrated|left=…|right=…</c>.</summary>
    public string Serialize()
    {
        var b = new StringBuilder(Magic);
        b.Append("|on=").Append(Enabled ? '1' : '0');
        Write(b, "rudder", Rudder); Write(b, "left", LeftBrake); Write(b, "right", RightBrake);
        return b.ToString();
    }

    private static void Write(StringBuilder b, string tag, PedalAxisCal c)
    {
        var ci = CultureInfo.InvariantCulture;
        b.Append('|').Append(tag).Append('=').Append(c.Axis.ToString(ci)).Append(',').Append(c.Invert ? '1' : '0')
         .Append(',').Append(c.Dead.ToString("R", ci)).Append(',').Append(c.Min.ToString("R", ci)).Append(',').Append(c.Max.ToString("R", ci))
         .Append(',').Append(c.Center.ToString("R", ci)).Append(',').Append(c.Calibrated ? '1' : '0');
    }

    /// <summary>The profile written by <see cref="Serialize"/>, or null for anything else (a corrupt or older
    /// entry is ignored, and the device starts from its defaults).</summary>
    public static PedalProfile? Parse(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        string[] parts = s!.Split('|');
        if (parts.Length < 5 || parts[0] != Magic) return null;
        var p = new PedalProfile();
        int got = 0;
        for (int i = 1; i < parts.Length; i++)
        {
            int eq = parts[i].IndexOf('=');
            if (eq < 0) return null;
            string k = parts[i].Substring(0, eq), v = parts[i].Substring(eq + 1);
            switch (k)
            {
                case "on": p.Enabled = v == "1"; got |= 1; break;
                case "rudder": if (!Read(v, p.Rudder)) return null; got |= 2; break;
                case "left": if (!Read(v, p.LeftBrake)) return null; got |= 4; break;
                case "right": if (!Read(v, p.RightBrake)) return null; got |= 8; break;
            }
        }
        return got == 15 ? p : null;
    }

    private static bool Read(string v, PedalAxisCal c)
    {
        string[] f = v.Split(',');
        if (f.Length != 7) return false;
        var ci = CultureInfo.InvariantCulture;
        const NumberStyles fl = NumberStyles.Float;
        if (!int.TryParse(f[0], NumberStyles.Integer, ci, out c.Axis)) return false;
        c.Invert = f[1] == "1";
        if (!double.TryParse(f[2], fl, ci, out c.Dead) || !double.TryParse(f[3], fl, ci, out c.Min)
            || !double.TryParse(f[4], fl, ci, out c.Max) || !double.TryParse(f[5], fl, ci, out c.Center)) return false;
        c.Calibrated = f[6] == "1";
        return !double.IsNaN(c.Min) && !double.IsNaN(c.Max) && !double.IsNaN(c.Center) && !double.IsNaN(c.Dead);
    }
}

/// <summary>This frame's pedals. Inactive = nothing connected, switched off, or being calibrated: the hand
/// controls then fly exactly as they did before pedals existed.</summary>
public struct PedalFrame
{
    public bool Active;
    /// <summary>−1…1, + = right pedal forward.</summary>
    public double Rudder;
    /// <summary>0…1 each, 1 = fully pressed.</summary>
    public double LeftBrake, RightBrake;
}

public static class PedalMapper
{
    public static PedalFrame Read(PedalProfile p, IReadOnlyList<double> axes)
    {
        var f = new PedalFrame { Active = true };
        if (p.Rudder.Set && p.Rudder.Axis < axes.Count) f.Rudder = p.Rudder.Centred(axes[p.Rudder.Axis]);
        if (p.LeftBrake.Set && p.LeftBrake.Axis < axes.Count) f.LeftBrake = p.LeftBrake.OneSided(axes[p.LeftBrake.Axis]);
        if (p.RightBrake.Set && p.RightBrake.Axis < axes.Count) f.RightBrake = p.RightBrake.OneSided(axes[p.RightBrake.Axis]);
        return f;
    }
}

/// <summary>The pedals joined to the hand controls, in the sim's own terms.</summary>
public static class PedalBlend
{
    /// <summary>The rudder: pedals and hand add (each its own offset), the total clamped. No pedals = the hand's.</summary>
    public static double Rudder(double hand, in PedalFrame p) => p.Active ? Math.Clamp(hand + p.Rudder, -1.0, 1.0) : hand;

    /// <summary>Two toe brakes as the sim's (BrakeInput, BrakeBias): the sim brakes the left wheel
    /// brake × (1 − bias) and the right brake × (1 + bias), clamped (LandingGear.Compute), so brake = (L + R) / 2 and
    /// bias = (R − L) / (L + R) give back exactly L and R.</summary>
    public static (double Brake, double Bias) ToSim(double left, double right)
    {
        double l = Math.Clamp(left, 0.0, 1.0), r = Math.Clamp(right, 0.0, 1.0), sum = l + r;
        if (sum < 1e-9) return (0.0, 0.0);
        return (0.5 * sum, (r - l) / sum);
    }

    /// <summary>What the sim makes of (brake, bias) on each wheel: the inverse of <see cref="ToSim"/>.</summary>
    public static (double Left, double Right) FromSim(double brake, double bias) =>
        (Math.Clamp(brake * (1.0 - bias), 0.0, 1.0), Math.Clamp(brake * (1.0 + bias), 0.0, 1.0));

    /// <summary>The hand's brake (a trigger, the pad's band, a joystick button — biased by the hand's rudder) and the
    /// toe brakes together: each wheel gets the harder of the two. No pedals = the hand's, unchanged.</summary>
    public static (double Brake, double Bias) Combine(double handBrake, double handBias, in PedalFrame p)
    {
        if (!p.Active) return (handBrake, handBias);
        (double hl, double hr) = FromSim(handBrake, handBias);
        return ToSim(Math.Max(hl, p.LeftBrake), Math.Max(hr, p.RightBrake));
    }
}

/// <summary>
/// THE GUIDED CALIBRATION, the pedals' version of the joystick setup: rest → push the RIGHT pedal (finds the rudder
/// axis and which way it runs) → press the LEFT toe brake → the RIGHT toe brake (finds each brake's axis and which
/// way it runs) → sweep everything through its full travel while the ends are learnt live → Finish. Every step can
/// be skipped (the axis it would find stays as it was). Ends are tracked from the first frame, so the sweep only
/// has to fill in what the earlier steps did not reach.
/// </summary>
public sealed class PedalCalibrator
{
    public enum Step { Rest, RudderRight, LeftToe, RightToe, Sweep }

    public const double RestSec = 1.0, HoldSec = 0.4, MoveThreshold = 0.35, StillPerFrame = 0.01;

    private readonly PedalProfile _p;
    private readonly int _n;
    private readonly double[] _rest, _min, _max, _last;
    private double _still;
    private int _candidate = -1;
    private bool _first = true;

    public Step Current { get; private set; } = Step.Rest;
    public double Progress { get; private set; }
    public string Status { get; private set; } = "";
    /// <summary>The axis the current step is looking at (−1 none), for a highlight.</summary>
    public int Focus { get; private set; } = -1;

    public PedalCalibrator(PedalProfile start, int axisCount)
    {
        _p = start.Clone();
        _n = Math.Max(0, axisCount);
        _rest = new double[_n]; _min = new double[_n]; _max = new double[_n]; _last = new double[_n];
    }

    public string Instruction => Current switch
    {
        Step.Rest => "Feet OFF the pedals, rudder centred. Hold still.",
        Step.RudderRight => "Push the RIGHT rudder pedal fully forward and hold.",
        Step.LeftToe => "Centre the rudder. Press the LEFT toe brake fully and hold (or Skip).",
        Step.RightToe => "Now the RIGHT toe brake, fully, and hold (or Skip).",
        _ => "Sweep: full left and right rudder, each toe brake to the floor, a few times. Then Finish.",
    };

    /// <summary>The same, short enough for one row of the VR menu (about 40 characters).</summary>
    public string ShortInstruction => Current switch
    {
        Step.Rest => "Feet off the pedals. Hold still.",
        Step.RudderRight => "Push the RIGHT pedal fully, hold.",
        Step.LeftToe => "Centre. LEFT toe brake fully, hold.",
        Step.RightToe => "Now the RIGHT toe brake fully, hold.",
        _ => "Sweep everything end to end, then FINISH.",
    };

    /// <summary>The smallest and largest raw value seen on an axis so far (for the live readout).</summary>
    public (double Min, double Max) Seen(int axis) => axis >= 0 && axis < _n ? (_min[axis], _max[axis]) : (0.0, 0.0);

    public void Tick(double dt, IReadOnlyList<double> raw)
    {
        int n = Math.Min(_n, raw.Count);
        double step = 0.0;
        for (int a = 0; a < n; a++)
        {
            double v = raw[a];
            if (_first) { _min[a] = _max[a] = _last[a] = _rest[a] = v; }
            step = Math.Max(step, Math.Abs(v - _last[a]));
            _last[a] = v;
            if (v < _min[a]) _min[a] = v;
            if (v > _max[a]) _max[a] = v;
        }
        _first = false;
        bool still = step < StillPerFrame;
        _still = still ? _still + dt : 0.0;
        switch (Current)
        {
            case Step.Rest:
                // Rest = where everything settles once it has been still (a moving frame starts it again).
                if (_still < 1e-9) for (int a = 0; a < n; a++) _rest[a] = _last[a];
                for (int a = 0; a < n; a++) _rest[a] += 0.1 * (_last[a] - _rest[a]);
                Progress = Math.Clamp(_still / RestSec, 0.0, 1.0);
                Status = still ? "HOLD STILL…" : "WAITING FOR STILLNESS";
                if (_still >= RestSec) Enter(Step.RudderRight);
                break;
            case Step.RudderRight:
            {
                int best = Moved(n, -1, -1);
                if (Hold(best))
                {
                    _p.Rudder.Axis = best;
                    _p.Rudder.Invert = _last[best] < _rest[best];
                    Enter(Step.LeftToe);
                }
                break;
            }
            case Step.LeftToe:
            {
                int best = Moved(n, _p.Rudder.Axis, -1);
                if (Hold(best)) { _p.LeftBrake.Axis = best; Enter(Step.RightToe); }
                break;
            }
            case Step.RightToe:
            {
                int best = Moved(n, _p.Rudder.Axis, _p.LeftBrake.Axis);
                if (Hold(best)) { _p.RightBrake.Axis = best; Enter(Step.Sweep); }
                break;
            }
            case Step.Sweep:
                Focus = -1;
                Progress = 1.0;
                Status = "LEARNING THE ENDS…";
                break;
        }
    }

    /// <summary>The axis (other than the two excluded) furthest from rest, if any is past the threshold.</summary>
    private int Moved(int n, int notA, int notB)
    {
        int best = -1;
        double bestD = MoveThreshold;
        for (int a = 0; a < n; a++)
        {
            if (a == notA || a == notB) continue;
            double d = Math.Abs(_last[a] - _rest[a]);
            if (d > bestD) { bestD = d; best = a; }
        }
        return best;
    }

    private bool Hold(int best)
    {
        if (best != _candidate) { _candidate = best; _still = 0.0; }
        Focus = best;
        Progress = best < 0 ? 0.0 : Math.Clamp(_still / HoldSec, 0.0, 1.0);
        Status = best < 0 ? "ALL THE WAY" : "AXIS " + (best + 1).ToString(CultureInfo.InvariantCulture) + " — HOLD…";
        return best >= 0 && _still >= HoldSec;
    }

    private void Enter(Step s)
    {
        Current = s;
        _still = 0.0; _candidate = -1; Progress = 0.0; Focus = -1; Status = "";
    }

    /// <summary>Skip this step: the axis it would have found keeps its current assignment.</summary>
    public void Skip()
    {
        if (Current == Step.Rest) { Enter(Step.RudderRight); return; }
        if (Current != Step.Sweep) Enter(Current + 1);
    }

    /// <summary>The profile with what was learnt: each assigned axis's ends (if it travelled at least 0.2), the
    /// rudder's centre at rest, and each brake's direction from where it rested.</summary>
    public PedalProfile Finish()
    {
        var p = _p.Clone();
        Learn(p.Rudder, centring: true);
        Learn(p.LeftBrake, centring: false);
        Learn(p.RightBrake, centring: false);
        return p;
    }

    private void Learn(PedalAxisCal c, bool centring)
    {
        if (!c.Set || c.Axis >= _n || _first) return;
        int a = c.Axis;
        if (_max[a] - _min[a] < 0.2) return;   // never moved: keep what it had
        c.Min = _min[a]; c.Max = _max[a];
        if (centring) c.Center = Math.Clamp(_rest[a], c.Min, c.Max);
        else c.Invert = Math.Abs(_rest[a] - c.Max) < Math.Abs(_rest[a] - c.Min);   // it rested at the top: pressing lowers it
        c.Calibrated = true;
    }
}

/// <summary>Where profiles live (PlayerPrefs in the game, a dictionary in the tests).</summary>
public interface IPedalStore
{
    string? Load(string key);
    void Save(string key, string value);
}

/// <summary>
/// The pedals over time: which connected device is the pedals (hot-plug, the pilot's own choice), its profile
/// (loaded, defaulted, edited, saved), the calibration while it runs, and this frame's <see cref="Frame"/>. Every
/// device that appears or goes is logged once with its name, VID:PID, backend and axes (the Bridge prefixes
/// nothing: the lines already start "[Pedals]", which is what logcat is grepped for). Allocates only when a device
/// comes or goes, or the pilot changes something.
/// </summary>
public sealed class PedalSession
{
    public const string ProfilePrefix = "pedals.profile.", ChoiceKey = "pedals.device";

    private readonly IPedalStore _store;
    private readonly Action<string>? _log;
    private readonly List<PedalDevice> _known = new();
    private string _choice;

    public PedalDevice? Device { get; private set; }
    public PedalProfile Profile { get; private set; } = new();
    public PedalMatch Match { get; private set; }
    public PedalFrame Frame { get; private set; }
    public PedalCalibrator? Calibration { get; private set; }
    /// <summary>Every game controller seen now (the menu's device choice cycles through them).</summary>
    public IReadOnlyList<PedalDevice> Devices => _known;

    public bool Connected => Device != null;
    public bool Active => Frame.Active;

    public PedalSession(IPedalStore store, Action<string>? log = null)
    {
        _store = store;
        _log = log;
        _choice = store.Load(ChoiceKey) ?? "";
    }

    /// <summary>The device the pilot picked by hand ("" = detect).</summary>
    public string Choice => _choice;

    /// <summary>One frame: <paramref name="devices"/> is every game controller connected now, axes already read.</summary>
    public void Update(IReadOnlyList<PedalDevice> devices, double dt)
    {
        if (Changed(devices)) Replug(devices);
        PedalDevice? d = Device;
        if (d != null && Calibration != null) Calibration.Tick(dt, d.Axes);
        Frame = d != null && Profile.Enabled && Calibration == null ? PedalMapper.Read(Profile, d.Axes) : default;
    }

    private bool Changed(IReadOnlyList<PedalDevice> devices)
    {
        if (devices.Count != _known.Count) return true;
        for (int i = 0; i < devices.Count; i++) if (!ReferenceEquals(devices[i], _known[i])) return true;
        return false;
    }

    private void Replug(IReadOnlyList<PedalDevice> devices)
    {
        foreach (PedalDevice old in _known)
            if (!Contains(devices, old)) Log($"[Pedals] disconnected: \"{old.Name}\" {old.VidPid} ({old.Backend})");
        foreach (PedalDevice d in devices)
            if (!Contains(_known, d))
            {
                PedalMatch m = PedalCatalog.Classify(d);
                Log($"[Pedals] connected: \"{d.Name}\" {d.VidPid} via {d.Backend}, {d.AxisNames.Length} axes [{string.Join(" ", d.AxisNames)}], " +
                    $"{d.ButtonCount} buttons -> {(m.IsPedals ? "PEDALS" : "not pedals")} (score {m.Score}: {m.Reason})");
            }
        _known.Clear();
        _known.AddRange(devices);
        Select();
    }

    private static bool Contains(IReadOnlyList<PedalDevice> list, PedalDevice d)
    {
        for (int i = 0; i < list.Count; i++) if (ReferenceEquals(list[i], d)) return true;
        return false;
    }

    /// <summary>Pick the pedals among the known devices: the pilot's choice if it is here, else the best-scoring
    /// device that looks like pedals, else none (the controllers keep the rudder and brakes).</summary>
    private void Select(bool force = false)
    {
        PedalDevice? pick = null;
        int best = int.MinValue;
        foreach (PedalDevice d in _known)
        {
            if (_choice.Length > 0 && d.Key == _choice) { pick = d; Match = PedalCatalog.Classify(d); break; }
            PedalMatch m = PedalCatalog.Classify(d);
            if (m.IsPedals && m.Score > best) { best = m.Score; pick = d; Match = m; }
        }
        if (!force && ReferenceEquals(pick, Device)) return;
        bool had = Device != null;
        Device = pick;
        Calibration = null;
        if (pick == null)
        {
            Profile = new PedalProfile();
            Match = default;
            if (had) Log("[Pedals] no pedals now: rudder and brakes back on the controllers");
            return;
        }
        PedalProfile? saved = PedalProfile.Parse(_store.Load(ProfilePrefix + pick.Key));
        Profile = saved ?? PedalCatalog.DefaultFor(pick);
        Log($"[Pedals] using \"{pick.Name}\" {pick.VidPid} ({(saved != null ? "saved profile" : "defaults")}): {MappingText()}");
    }

    public string MappingText() =>
        $"rudder {AxisName(Profile.Rudder)}{(Profile.Rudder.Invert ? " inverted" : "")}, left brake {AxisName(Profile.LeftBrake)}{(Profile.LeftBrake.Invert ? " inverted" : "")}, " +
        $"right brake {AxisName(Profile.RightBrake)}{(Profile.RightBrake.Invert ? " inverted" : "")}{(Profile.Enabled ? "" : " (OFF)")}";

    public string AxisName(PedalAxisCal c)
    {
        if (!c.Set) return "none";
        PedalDevice? d = Device;
        return d != null && c.Axis < d.AxisNames.Length ? d.AxisNames[c.Axis].ToUpperInvariant() : "AXIS " + (c.Axis + 1).ToString(CultureInfo.InvariantCulture);
    }

    private void Log(string s) => _log?.Invoke(s);

    private void Save()
    {
        if (Device != null) _store.Save(ProfilePrefix + Device.Key, Profile.Serialize());
    }

    // ---- what the options page and the VR menu change ----

    /// <summary>Step the device choice: detect → each connected game controller → detect … Persisted.</summary>
    public void CycleDevice(int dir)
    {
        var keys = new List<string> { "" };
        foreach (PedalDevice d in _known) if (!d.IsGamepad && !keys.Contains(d.Key)) keys.Add(d.Key);
        int i = Math.Max(0, keys.IndexOf(_choice));
        _choice = keys[((i + dir) % keys.Count + keys.Count) % keys.Count];
        _store.Save(ChoiceKey, _choice);
        Select(force: true);   // re-select (and log) with the new choice
    }

    public void SetEnabled(bool on)
    {
        if (Device == null) return;
        Profile.Enabled = on;
        Save();
        Log($"[Pedals] {(on ? "on" : "OFF: the controllers keep the rudder and brakes")}");
    }

    /// <summary>Next / previous axis for a role (none, axis 1 … n), skipping none of them: the pilot sees the bar move.</summary>
    public void CycleAxis(PedalRole role, int dir)
    {
        PedalDevice? d = Device;
        if (d == null) return;
        int n = d.Axes.Length;
        PedalAxisCal c = Profile[role];
        int next = ((c.Axis + 1 + dir) % (n + 1) + (n + 1)) % (n + 1) - 1;   // −1 … n−1, wrapping
        c.Axis = next;
        (c.Min, c.Max) = next >= 0 && next < d.AxisNames.Length ? PedalCatalog.NominalRange(d.AxisNames[next]) : (-1.0, 1.0);
        c.Center = 0.5 * (c.Min + c.Max);
        c.Calibrated = false;
        Save();
    }

    public void ToggleInvert(PedalRole role)
    {
        if (Device == null) return;
        Profile[role].Invert = !Profile[role].Invert;
        Save();
    }

    /// <summary>Dead zone ± a step, 0 … 25 %.</summary>
    public void AdjustDead(PedalRole role, double delta)
    {
        if (Device == null) return;
        PedalAxisCal c = Profile[role];
        c.Dead = Math.Round(Math.Clamp(c.Dead + delta, 0.0, 0.25), 3);
        Save();
    }

    public void SetDead(PedalRole role, double dead)
    {
        if (Device == null) return;
        Profile[role].Dead = Math.Round(Math.Clamp(dead, 0.0, 0.25), 3);
        Save();
    }

    /// <summary>Back to the device's detected mapping, uncalibrated.</summary>
    public void ResetToDefaults()
    {
        if (Device == null) return;
        Profile = PedalCatalog.DefaultFor(Device);
        Calibration = null;
        Save();
        Log($"[Pedals] reset to defaults: {MappingText()}");
    }

    public void StartCalibration()
    {
        if (Device == null) return;
        Calibration = new PedalCalibrator(Profile, Device.Axes.Length);
        Frame = default;   // from this moment, not the next frame: nothing flies from pedals being learnt
    }

    public void SkipCalibrationStep() => Calibration?.Skip();

    public void CancelCalibration() => Calibration = null;

    public void FinishCalibration()
    {
        if (Calibration == null) return;
        Profile = Calibration.Finish();
        Calibration = null;
        Save();
        Log($"[Pedals] calibrated: {MappingText()}; " + RangeText(Profile.Rudder, "rudder") + RangeText(Profile.LeftBrake, " left") + RangeText(Profile.RightBrake, " right"));
    }

    private static string RangeText(PedalAxisCal c, string tag) => c.Set
        ? string.Format(CultureInfo.InvariantCulture, "{0} {1:F2}..{2:F2}{3}", tag, c.Min, c.Max, c.Calibrated ? "" : " (not moved)")
        : "";

    // ---- text for the two menus ----

    /// <summary>"Thrustmaster T.Flight Rudder Pedals  044F:B679" / "none: rudder and brakes on the controllers".</summary>
    public string DeviceLine()
    {
        PedalDevice? d = Device;
        if (d == null) return _known.Count == 0 ? "none connected" : "none detected (" + _known.Count.ToString(CultureInfo.InvariantCulture) + " other controller" + (_known.Count == 1 ? ")" : "s)");
        return d.Name + (d.VendorId != 0 || d.ProductId != 0 ? "  " + d.VidPid : "") + (_choice.Length > 0 ? "  (chosen)" : "");
    }

    /// <summary>"RZ inv  dz 3%  [----|##---] +42" — the role's axis, settings and live value.</summary>
    public string RoleLine(PedalRole role)
    {
        PedalAxisCal c = Profile[role];
        PedalDevice? d = Device;
        if (d == null) return "-";
        if (!c.Set) return "none";
        double raw = c.Axis < d.Axes.Length ? d.Axes[c.Axis] : 0.0;
        var ci = CultureInfo.InvariantCulture;
        string head = AxisName(c) + (c.Invert ? " inv" : "") + "  dz " + (c.Dead * 100.0).ToString("0", ci) + "%  ";
        string tail = c.Calibrated ? "" : "  (uncal.)";
        if (role == PedalRole.Rudder)
        {
            double v = c.Centred(raw);
            return head + Bar(v, true) + " " + (v * 100.0).ToString("+0;-0;0", ci) + tail;
        }
        double t = c.OneSided(raw);
        return head + Bar(t, false) + " " + (t * 100.0).ToString("0", ci) + "%" + tail;
    }

    /// <summary>"RZ inv [---|##--] +42": the VR menu's narrower row (no dead zone, a shorter bar).</summary>
    public string RoleCompact(PedalRole role)
    {
        PedalAxisCal c = Profile[role];
        PedalDevice? d = Device;
        if (d == null) return "-";
        if (!c.Set) return "none";
        double raw = c.Axis < d.Axes.Length ? d.Axes[c.Axis] : 0.0;
        var ci = CultureInfo.InvariantCulture;
        string head = AxisName(c) + (c.Invert ? " inv " : " ");
        if (role == PedalRole.Rudder)
        {
            double v = c.Centred(raw);
            return head + Bar(v, true, 4) + " " + (v * 100.0).ToString("+0;-0;0", ci);
        }
        double t = c.OneSided(raw);
        return head + Bar(t, false, 4) + " " + (t * 100.0).ToString("0", ci) + "%";
    }

    /// <summary>A text bar: centred "[----|##--]" (−1…1) or one-sided "[####------]" (0…1).</summary>
    public static string Bar(double v, bool centred, int half = 5)
    {
        var b = new StringBuilder(2 * half + 3);
        b.Append('[');
        if (centred)
        {
            int k = (int)Math.Round(Math.Clamp(v, -1.0, 1.0) * half);
            for (int i = -half; i < 0; i++) b.Append(k <= i ? '#' : '-');
            b.Append('|');
            for (int i = 1; i <= half; i++) b.Append(k >= i ? '#' : '-');
        }
        else
        {
            int k = (int)Math.Round(Math.Clamp(v, 0.0, 1.0) * 2 * half);
            for (int i = 1; i <= 2 * half; i++) b.Append(k >= i ? '#' : '-');
        }
        return b.Append(']').ToString();
    }

    /// <summary>The live frame in one line, for both menus: "RUD +42  L 0%  R 85%".</summary>
    public string LiveLine()
    {
        if (Device == null) return "";
        PedalFrame f = Profile.Enabled ? PedalMapper.Read(Profile, Device.Axes) : default;
        return string.Format(CultureInfo.InvariantCulture, "RUD {0:+0;-0;0}  L {1:0}%  R {2:0}%", f.Rudder * 100.0, f.LeftBrake * 100.0, f.RightBrake * 100.0);
    }
}

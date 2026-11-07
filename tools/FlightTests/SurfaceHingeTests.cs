using System.Text.Json;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The Pitts' flight surfaces move (Ty t917u 2026-11-06: "The elevator on the pitts is not visibly moving").
///
/// The fault: since its cockpit rig arrived (2026-11-02) the Pitts skips the hinge carve, and the rig has no rows for the
/// elevators, ailerons or rudder, so all seven surfaces were drawn still. The Bridge now turns each named surface about
/// the hinge line in <see cref="SurfaceHinges"/>. What is held here, headless:
///   1. the table against the DRAWN meshes, in every Pitts livery's GLB: the elevators' faces 4 mm aft of their hinge,
///      the ailerons' 3 mm aft of theirs, each face's mid-height on the line, the rudder's round nose centred on its
///      hinge (gaps and radii are build_pitts_s2b.py's, copied by hand);
///   2. the slave-strut pins on the strut's ends and on their ailerons' horns;
///   3. every own model's named flight surfaces moved by SOMETHING (its rig, or this table), with the one known gap
///      (the 172) listed by name so fixing it, or adding another, is a deliberate edit;
///   4. the sim side: full aft stick gives a full trailing-edge-UP deflection on the Pitts, full forward full down, and
///      half stick lands in between. The Unity check ControlSurfaceMotionCheck measures that those deflections reach
///      the drawn surfaces (both halves, the right way, proportional).
/// </summary>
public class SurfaceHingeTests
{
    private readonly ITestOutputHelper _o;
    public SurfaceHingeTests(ITestOutputHelper o) { _o = o; }

    private static readonly string[] PittsSuffixes = { "-own-checker-unmarked", "-own-factory-unmarked", "-own", "-own-n1191" };
    private static string PittsGlb(string suffix) => Path.Combine(OwnPittsModelTests.Models, "pitts-s2b-like", "pitts-s2b-like" + suffix + ".glb");

    private static (double x, double y, double z) At(SurfaceHinges.Line l, double t) =>
        (l.X0 + t * (l.X1 - l.X0), l.Y0 + t * (l.Y1 - l.Y0), l.Z0 + t * (l.Z1 - l.Z0));

    [Fact]
    public void ThePittsTableNamesAllSevenSurfaces()
    {
        var names = SurfaceHinges.For("pitts-s2b-like").Select(l => l.Node + ":" + l.Surface).OrderBy(s => s).ToArray();
        Assert.Equal(new[] { "Aileron_LL:aileron", "Aileron_LR:aileron", "Aileron_UL:aileron", "Aileron_UR:aileron",
                             "Elevator_L:elevator", "Elevator_R:elevator", "Rudder:rudder" }, names);
        Assert.Empty(SurfaceHinges.For("extra-300-like"));   // its markers and rig rows do it
        Assert.Empty(SurfaceHinges.For(null));
    }

    [Theory]
    [InlineData("-own-checker-unmarked")]
    [InlineData("-own-factory-unmarked")]
    [InlineData("-own")]
    [InlineData("-own-n1191")]
    public void EachHingeLiesOnItsDrawnSurface(string suffix)
    {
        string path = PittsGlb(suffix);
        // The table is in each node's own frame, which is the model's only while the node carries no transform.
        foreach (InstrumentLibraryTests.Glb.Node n in InstrumentLibraryTests.Glb.Nodes(path))
        {
            if (!SurfaceHinges.For("pitts-s2b-like").Any(l => l.Node == n.Name)) continue;
            Assert.True(n.Matrix == null && n.Translation.All(v => v == 0) && n.Rotation.Take(3).All(v => v == 0) && n.Scale.All(v => v == 1),
                $"{suffix} {n.Name}: the node has a transform of its own; SurfaceHinges is in the node's frame");
        }
        var verts = OwnPittsModelTests.Vertices(path);
        foreach (SurfaceHinges.Line l in SurfaceHinges.For("pitts-s2b-like"))
        {
            Assert.True(verts.ContainsKey(l.Node), $"{suffix}: no {l.Node}");
            var v = verts[l.Node];
            if (l.Surface == "rudder")
            {
                // A half-cylinder about the hinge: its nose reaches one post radius ahead, symmetric about the centreline.
                var nose = v.Where(p => p.x < l.X0).ToList();
                Assert.NotEmpty(nose);
                double ahead = l.X0 - nose.Min(p => p.x), zLo = v.Min(p => p.z), zHi = v.Max(p => p.z);
                _o.WriteLine($"{suffix} Rudder: nose {ahead * 1000:F1} mm ahead of the hinge, z {zLo:F4}..{zHi:F4}");
                Assert.InRange(ahead, 0.020, 0.040);
                Assert.InRange(zLo + zHi, -0.001, 0.001);
                // Every nose point is within its post radius of the line: the nose turns in place.
                foreach (var p in nose) Assert.True(Math.Sqrt(Math.Pow(p.x - l.X0, 2) + p.z * p.z) <= ahead + 0.002, $"{suffix} Rudder nose point off the hinge circle");
                Assert.InRange(v.Min(p => p.y), l.Y0 - 0.005, l.Y0 + 0.005);
                Assert.InRange(v.Max(p => p.y), l.Y1 - 0.005, l.Y1 + 0.005);
                continue;
            }
            double gap = l.Surface == "elevator" ? 0.004 : 0.003;   // build_pitts_s2b.py: x_from = H_T - 0.004 / Hf - GAP
            // Station by station (the mesh is lofted on span stations): the front face sits `gap` aft of the line and its
            // mid-height on it. A station carrying a horn (the slave struts' fittings, which run forward of the hinge) is
            // the one place the front of the mesh is not the face, so stations whose front reaches ahead of the line skip.
            var stations = v.Select(p => Math.Round(p.z, 3)).Distinct().ToList();
            int checkedN = 0;
            foreach (double z in stations)
            {
                double t = (z - l.Z0) / (l.Z1 - l.Z0);
                if (t < -0.01 || t > 1.01) continue;
                var (hx, hy, _) = At(l, t);
                var sl = v.Where(p => Math.Abs(p.z - z) < 0.0015).ToList();
                double front = sl.Min(p => p.x);
                if (front < hx - 0.001) continue;   // a horn
                var face = sl.Where(p => p.x < front + 0.004).ToList();
                double mid = 0.5 * (face.Min(p => p.y) + face.Max(p => p.y));
                // The inboard ends of the lower ailerons are lofted from the hinge itself (a +0.004 m span inset), so the
                // gap there is 0 to `gap`.
                Assert.True(front - hx > -0.001 && front - hx < gap + 0.001, $"{suffix} {l.Node} z {z:F3}: front {front:F4}, hinge {hx:F4}");
                Assert.True(Math.Abs(mid - hy) < 0.002, $"{suffix} {l.Node} z {z:F3}: face mid {mid:F4}, hinge {hy:F4}");
                checkedN++;
            }
            Assert.True(checkedN >= 4, $"{suffix} {l.Node}: only {checkedN} stations checked");
        }
    }

    [Fact]
    public void TheStrutPinsAreOnTheStrutEndsAndTheirHorns()
    {
        var verts = OwnPittsModelTests.Vertices(PittsGlb("-own"));
        foreach (SurfaceHinges.Pin pin in SurfaceHinges.PinsFor("pitts-s2b-like"))
        {
            var strut = verts[pin.Strut];
            bool upper = pin.Node.EndsWith("PinUpper");
            double endY = upper ? strut.Max(p => p.y) : strut.Min(p => p.y);
            var end = strut.Where(p => Math.Abs(p.y - endY) < 0.01).ToList();
            double ex = end.Average(p => p.x), ey = end.Average(p => p.y), ez = end.Average(p => p.z);
            double dEnd = Math.Sqrt(Math.Pow(pin.X - ex, 2) + Math.Pow(pin.Y - ey, 2) + Math.Pow(pin.Z - ez, 2));
            Assert.True(dEnd < 0.005, $"{pin.Node}: {dEnd * 1000:F1} mm from its strut's end");
            // On the aileron it hangs under: within the horn's reach (72 mm arm + eye) of that aileron's hinge.
            SurfaceHinges.Line l = SurfaceHinges.For("pitts-s2b-like").Single(x => x.Node == pin.On);
            double t = (pin.Z - l.Z0) / (l.Z1 - l.Z0);
            Assert.InRange(t, 0.0, 1.0);
            var (hx, hy, hz) = At(l, t);
            double dHinge = Math.Sqrt(Math.Pow(pin.X - hx, 2) + Math.Pow(pin.Y - hy, 2) + Math.Pow(pin.Z - hz, 2));
            _o.WriteLine($"{pin.Node}: {dEnd * 1000:F1} mm from the strut end, {dHinge * 1000:F0} mm from {pin.On}'s hinge");
            Assert.InRange(dHinge, 0.03, 0.10);
            Assert.Contains(verts[pin.On], p => Math.Abs(p.z - pin.Z) < 0.01 && p.x < hx - 0.03);   // the horn is there
        }
    }

    /// <summary>Own models whose flight surfaces nothing moves, by name. The 172's rig (c172-like-own-rig.json) has no
    /// rows for its surfaces either; its nodes DO sit on their hinges, so it wants rig rows, not this table. Found
    /// 2026-11-06 by ControlSurfaceMotionCheck, not fixed here (Pitts-only lane): NEEDS YOU.</summary>
    private static readonly HashSet<string> KnownStill = new() { "c172-like-own" };

    [Fact]
    public void EveryRiggedOwnModelsFlightSurfacesAreMovedBySomething()
    {
        string docs = Path.Combine(OwnPittsModelTests.Repo, "docs", "models");
        var stillFound = new List<string>();
        foreach (string rigPath in Directory.GetFiles(docs, "*-rig.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(rigPath));
            string model = doc.RootElement.GetProperty("model").GetString()!;
            string type = model.EndsWith("-own") ? model[..^4] : model;
            string dir = Path.Combine(OwnPittsModelTests.Models, type);
            string glb = Path.Combine(dir, model + ".glb");
            if (!File.Exists(glb)) continue;
            var rowNodes = doc.RootElement.GetProperty("rig").EnumerateArray()
                .Where(r => r.GetProperty("param").GetString() is "aileron" or "elevator" or "rudder")
                .Select(r => r.GetProperty("node").GetString()!).ToHashSet();
            var table = SurfaceHinges.For(type).Select(l => l.Node).ToHashSet();
            var still = new List<string>();
            foreach (InstrumentLibraryTests.Glb.Node n in InstrumentLibraryTests.Glb.Nodes(glb))
            {
                string l = n.Name.ToLowerInvariant();
                if (l.Contains("hingeaxis") || l.Contains("cable") || l.Contains("trim") || l.Contains("pedal") || l.Contains("tab")) continue;
                bool surface = l.StartsWith("aileron") || l.StartsWith("elevator") || l.StartsWith("rudder");
                if (!surface) continue;
                bool moved = rowNodes.Contains(n.Name) || table.Contains(n.Name)
                             || rowNodes.Any(r => n.Name.StartsWith(r + "_") || r.StartsWith(n.Name + "_"));   // a _Pivot parent row
                if (!moved) still.Add(n.Name);
            }
            _o.WriteLine($"{model}: {(still.Count == 0 ? "every flight surface moved" : "STILL " + string.Join(", ", still))}");
            if (still.Count > 0) stillFound.Add(model);
        }
        Assert.Contains("pitts-s2b-like-own", Directory.GetFiles(docs, "*-rig.json").Select(p => JsonDocument.Parse(File.ReadAllText(p)).RootElement.GetProperty("model").GetString()));
        Assert.Equal(KnownStill.OrderBy(s => s), stillFound.OrderBy(s => s));
    }

    // ---- the sim side: what the drawn surface is asked to show ------------------------------------------------------

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    /// <summary>The Pitts' elevator after holding <paramref name="stick"/> for 1.5 s at its spawn speed, rad (+ = TE down).
    /// Also the pitch rate that results, so the sense is the aeroplane's, not a sign read off a table.</summary>
    private static (double ele, double q) Hold(double stick) { var r = HoldAc(stick); return (r.ac.CurrentDeflections.ElevatorRad, r.q); }

    private static (Aircraft ac, double q) HoldAc(double stick)
    {
        AircraftConfig cfg = Load("pitts-s2b-like");
        double v = cfg.SpawnIasMs > 1 ? cfg.SpawnIasMs : 50, alt = 1500;
        var s = new RigidBodyState(new Vec3(0, 0, -alt), new Quat(0, 0, 0, 1), new Vec3(v, 0, 0), Vec3.Zero);
        var ac = new Aircraft(cfg, s, new ControlDeflections(0, 0, 0, 0));
        double qMax = 0;
        for (double t = 0; t < 1.5; t += SimLoop.DefaultFixedDtSec)
        {
            ac.Step(new ControlInputs(0, stick, 0, 0.6), SimLoop.DefaultFixedDtSec);
            if (t < 0.6) qMax = Math.Abs(ac.State.Rates.Y) > Math.Abs(qMax) ? ac.State.Rates.Y : qMax;
        }
        return (ac, qMax);
    }

    [Fact]
    public void AReplayShowsTheSurfacesAsFlown()
    {
        // The replay records CurrentDeflections and puts them back on the aeroplane, which AirframeVisual then draws
        // through the same SetDeflections as live: so the drawn elevator in a replay is the flown one if this round trip is.
        (Aircraft flown, _) = HoldAc(-1);
        ControlDeflections d = flown.CurrentDeflections;
        var frame = FlyingGame.Sim.Replay.ReplayFrame.Capture(1.5, flown, Vec3.Zero);
        (Aircraft shown, _) = HoldAc(0);
        Assert.True(Math.Abs(shown.CurrentDeflections.ElevatorRad - d.ElevatorRad) > 0.3);
        frame.ApplyTo(shown);
        Assert.Equal(d.ElevatorRad, shown.CurrentDeflections.ElevatorRad, 6);
        Assert.Equal(d.AileronRad, shown.CurrentDeflections.AileronRad, 6);
        Assert.Equal(d.RudderRad, shown.CurrentDeflections.RudderRad, 6);
        // ... and half way between two frames, half way between their surfaces.
        var mid = FlyingGame.Sim.Replay.FlightRecorder.Lerp(FlyingGame.Sim.Replay.ReplayFrame.Capture(0, HoldAc(0).ac, Vec3.Zero), frame, 0.5);
        mid.ApplyTo(shown);
        Assert.Equal(0.5 * d.ElevatorRad, shown.CurrentDeflections.ElevatorRad, 3);
    }

    [Fact]
    public void PittsStickGivesAFullProportionalElevator()
    {
        AircraftConfig cfg = Load("pitts-s2b-like");
        double max = cfg.Controls.Elevator.MaxDeflRad;
        var aft = Hold(-1); var fwd = Hold(1); var halfAft = Hold(-0.5); var zero = Hold(0);
        _o.WriteLine($"Pitts elevator (max {max * 57.3:F1} deg): stick -1 {aft.ele * 57.3:F1} deg (q {aft.q * 57.3:F1} deg/s), " +
                     $"-0.5 {halfAft.ele * 57.3:F1}, 0 {zero.ele * 57.3:F2}, +1 {fwd.ele * 57.3:F1} deg (q {fwd.q * 57.3:F1} deg/s)");
        // One stick sense pitches the nose up; that one must be trailing edge UP (negative), the other DOWN.
        var up = aft.q > fwd.q ? aft : fwd; var down = aft.q > fwd.q ? fwd : aft;
        Assert.True(up.q > 0 && down.q < 0, "full stick each way should pitch each way");
        Assert.True(up.ele < -0.8 * max, $"nose-up stick: {up.ele * 57.3:F1} deg, want TE up near {-max * 57.3:F1}");
        Assert.True(down.ele > 0.8 * max, $"nose-down stick: {down.ele * 57.3:F1} deg, want TE down near {max * 57.3:F1}");
        // Proportional in between (expo 0.1 bends it a little; the trim offset is small).
        double half = aft.q > fwd.q ? halfAft.ele : -halfAft.ele;
        Assert.InRange(half / up.ele, 0.3, 0.65);
        Assert.InRange(Math.Abs(zero.ele), 0.0, 0.05);
    }
}

using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Wreck;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-05 06:01 (iPhone 17 Pro, island-nav 2026.1105.0226): "I crashed in flight path view on the phone and the
/// shot went straight up into the sky?"
///
/// When a piece breaks off and lands, ChaseCamera leaves the flight path for its crash view (ChaseCamera.CrashView):
/// 30° above the wreck at the range that fits it, focus, range and position each eased over 2 s. These crash the
/// scripted Pitts (<see cref="PittsCrash"/>, the game's <c>-crashtest</c>) on the runway, on the grass, steeply onto the
/// runway and steeply into 4 m of water, and fly a frame-by-frame copy of that camera (Unity's SmoothDamp, 66.7 fps)
/// with the code as it was and as it is. As it was, the fitted range only ever grew and took in the eased focus's lag
/// and every renderer under the aircraft (the fuel sheen spreads at 0.5 m/s), so the camera kept climbing. Now it
/// must stay at a height set by the wreck, look down at it, and keep the airframe in the frame.
///
/// The radii, the span and the field of view are literals here on purpose (what ChaseCamera reads off the renderers
/// and the phone), as are the bounds, copied from the run this test was written against.
/// </summary>
[Collection("WorldTerrainActive")]
public class CrashCameraTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public CrashCameraTests(ITestOutputHelper o) { _o = o; }
    public void Dispose() { WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null; CrashEvents.ClearForTests(); }

    // Pitts S-2B: 6.1 m span, 5.7 m long (the chase distance is fitted to these).
    private const double PittsSpanM = 6.1, PittsLengthM = 5.7;
    // The airframe's drawn radius about its CG and a broken-off part's, as the renderers measure them.
    private const double AirframeRadiusM = 3.0, PieceRadiusM = 1.0;
    // The iPhone's chase camera: 60° vertical, portrait 9:19.5, so the narrower half-angle is the horizontal ≈ 15°.
    private const double HalfFovRad = 0.262;
    private const double SmoothS = 2.0, FrameS = 0.015;

    [Fact]
    public void ABrokenOffPieceOpensTheCrashViewOnLandOrWaterAndTheAirframeAloneNever()
    {
        var at = new Vec3(0, 0, -3);
        Assert.True(CrashFraming.OpensCrashView(new CrashImpact(ImpactSurface.Land, at, Vec3.Zero, 12, 80, 0.7, 3)));
        Assert.True(CrashFraming.OpensCrashView(new CrashImpact(ImpactSurface.Water, at, Vec3.Zero, 12, 80, 0.7, 3)));
        Assert.False(CrashFraming.OpensCrashView(new CrashImpact(ImpactSurface.Land, at, Vec3.Zero, 25, 1100, 1.0, -1)));
        Assert.False(CrashFraming.OpensCrashView(new CrashImpact(ImpactSurface.Water, at, Vec3.Zero, 0.6, 1100, 0.05, -1)));
    }

    [Fact]
    public void APartIsNeverFramedBiggerThanItsAeroplane()
    {
        Assert.Equal(3.0, CrashFraming.ClampRadiusM(3.0, PittsSpanM, PittsLengthM), 9);
        Assert.Equal(4.575, CrashFraming.ClampRadiusM(150 * Math.Sqrt(2), PittsSpanM, PittsLengthM), 9);   // a 150 m fuel sheen
    }

    /// <summary>The camera, frame by frame, as ChaseCamera.CrashView places it (Legacy: as it was until 2026-11-05).</summary>
    private sealed class CrashCam
    {
        public readonly bool Legacy;
        public Vec3 Pos, Focus, Bearing;
        private Vec3 _vFocus, _vPos;
        private double _range, _largest, _vRange;
        private bool _started;
        public CrashCam(bool legacy) { Legacy = legacy; }

        public void Frame(Vec3 focus, double radius, double chaseM, Vec3 startPos, Vec3 startLook, Vec3 target)
        {
            if (!_started)
            {
                _started = true;
                Pos = startPos; _range = (startPos - target).Length; _largest = _range;
                Focus = startPos + startLook * _range;
                Vec3 b = new(startPos.X - Focus.X, startPos.Y - Focus.Y, 0);
                if (!Legacy && b.Length < .25 * _range) b = new Vec3(startPos.X - focus.X, startPos.Y - focus.Y, 0);
                Bearing = b.Length < .1 ? new Vec3(-1, 0, 0) : b / b.Length;
            }
            Focus = SmoothDamp(Focus, focus, ref _vFocus, SmoothS, FrameS);
            double lag = (focus - Focus).Length;
            double want;
            // As it was (ChaseCamera before 2026-11-05, copied): the lag in the latched fit, and no ceiling.
            if (Legacy) { _largest = Math.Max(_largest, Math.Max(chaseM * 1.5, (radius + lag) * 1.15 / Math.Sin(HalfFovRad))); want = _largest; }
            else
            {
                _largest = Math.Max(_largest, CrashFraming.RangeM(radius, 0, HalfFovRad, chaseM));
                want = Math.Max(_largest, CrashFraming.RangeM(radius, lag, HalfFovRad, chaseM));
            }
            _range = SmoothDamp(_range, want, ref _vRange, SmoothS, FrameS);
            Pos = SmoothDamp(Pos, Focus + CrashFraming.CameraOffset(Bearing, new Vec3(0, 0, -1), _range), ref _vPos, SmoothS, FrameS);
        }
    }

    public sealed class Run
    {
        public double OpenedAt = double.NaN, SlickRadiusM;
        public int Pieces;
        // Over the last 10 s of the minute, per camera: highest above the wreck's focus, and at the end.
        public double LegacyMaxAboveM, LegacyEndAboveM, LegacyEndRangeM, MaxAboveM, EndAboveM, EndRangeM;
        // The fixed camera after its first 3 s: the steepest and shallowest look, the airframe's angle off the axis,
        // the least clearance over ground or water.
        public double MinPitchDeg = 90, MaxPitchDeg = -90, AirframeOffDeg, ClearanceM = double.PositiveInfinity;
    }

    private FuelSlick? _madeSlick;

    /// <summary>Crash the Pitts and fly both cameras for 60 s.</summary>
    private Run Crash(string scenario)
    {
        WorldTerrain.Active = new WorldTerrain();
        AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pitts-s2b-like.json"));
        RigidBodyState s0 = scenario is "steep" or "water" ? Steep(c, 70, 30, scenario == "water" ? 4.0 : 0.0) : PittsCrash.Start(c, scenario == "grass");
        var ac = new Aircraft(c, s0, ControlDeflections.Neutral);
        var relay = new CrashRelay(ac);
        var r = new Run();
        double t = 0, slickAt = double.NaN;
        CrashEvents.Impact += hit => { if (double.IsNaN(r.OpenedAt) && CrashFraming.OpensCrashView(hit)) r.OpenedAt = t; };

        double chase = ChaseFraming.FollowDistanceM(PittsSpanM, PittsLengthM);
        var legacy = new CrashCam(true); var cam = new CrashCam(false);
        var points = new List<Vec3>(); var masses = new List<double>(); var radii = new List<double>(); var scratch = new List<double>();
        var inputs = new ControlInputs(0, 0, 0, 1);
        const double dt = 0.005, seconds = 60;
        Vec3 startPos = Vec3.Zero, startLook = Vec3.Zero;
        for (int i = 0; i < (int)(seconds / dt); i++, t += dt)
        {
            if (double.IsNaN(r.OpenedAt))
            {
                // Until the view opens the camera is the flight-path camera: remember where it is for the hand-over.
                RigidBodyState p = ac.State;
                Vec3 g = p.Attitude.Rotate(p.Velocity), a = g - Atmosphere.WindAtPosition(p.Position);
                Vec3 follow = CameraReference.Direction(CameraReference.Mode.FlightPath, a, g, 4.0) ?? p.Attitude.Rotate(new Vec3(1, 0, 0));
                (startPos, startLook) = FlightPathPose(p, follow, chase, ChaseFraming.FollowHeightM(chase));
            }
            ac.Step(inputs, dt);
            relay.Step(dt);
            if (relay.Slick != null && double.IsNaN(slickAt)) slickAt = t;
            // The water crash breaks only gear off (an intact ditching: CrashRelay makes no slick). Make the slick a
            // break-up would, from the Pitts' own fuel, so the sheen under the aircraft spreads as it does on the phone.
            FuelSlick? slick = relay.Slick;
            if (scenario == "water" && !double.IsNaN(r.OpenedAt) && slick == null)
            {
                if (_madeSlick == null)
                {
                    var full = new AircraftLoad(c);   // full tanks: 79.04 kg on the Pitts, a 186.9 m slick after 300 s
                    foreach (var st in full.Slots) if (st.Kind == AircraftLoad.StationKind.Fuel) full.Set(st.Id, st.MaxKg);
                    _madeSlick = FuelSlick.TryCreate(c, full, true, true, ac.State.Position, Vec3.Zero);
                }
                slick = _madeSlick;
                if (double.IsNaN(slickAt)) slickAt = t;
            }
            if (double.IsNaN(r.OpenedAt) || i % 3 != 0) continue;

            RigidBodyState s = ac.State;
            // The fuel sheen hangs under the aircraft: a flat quad of the slick's radius, R·√2 across its bounds.
            r.SlickRadiusM = slick?.RadiusAt(t - slickAt) ?? 0;
            double drawn = Math.Max(AirframeRadiusM, r.SlickRadiusM * Math.Sqrt(2));
            foreach (CrashCam k in new[] { legacy, cam })
            {
                points.Clear(); masses.Clear(); radii.Clear();
                points.Add(s.Position); masses.Add(ac.MassProperties.MassKg);
                radii.Add(k.Legacy ? drawn : CrashFraming.ClampRadiusM(drawn, PittsSpanM, PittsLengthM));
                foreach (WreckPiece wp in relay.Wreck.Pieces) { points.Add(wp.State.Position); masses.Add(wp.MassKg); radii.Add(PieceRadiusM); }
                var (focus, radius) = CrashFraming.Cluster(points, masses, radii, scratch);
                k.Frame(focus, radius, chase, startPos, startLook, s.Position);
                double above = k.Focus.Z - k.Pos.Z;
                if (t >= seconds - 10)
                {
                    if (k.Legacy) { r.LegacyMaxAboveM = Math.Max(r.LegacyMaxAboveM, above); r.LegacyEndAboveM = above; r.LegacyEndRangeM = (k.Pos - k.Focus).Length; }
                    else { r.MaxAboveM = Math.Max(r.MaxAboveM, above); r.EndAboveM = above; r.EndRangeM = (k.Pos - k.Focus).Length; }
                }
                if (k.Legacy || t < r.OpenedAt + 3) continue;
                Vec3 look = (k.Focus - k.Pos).Normalized();
                double pitch = Math.Asin(Math.Clamp(-look.Z, -1, 1)) * 180 / Math.PI;
                r.MinPitchDeg = Math.Min(r.MinPitchDeg, pitch); r.MaxPitchDeg = Math.Max(r.MaxPitchDeg, pitch);
                r.AirframeOffDeg = Math.Max(r.AirframeOffDeg, Math.Acos(Math.Clamp((s.Position - k.Pos).Normalized().Dot(look), -1, 1)) * 180 / Math.PI);
                double surface = Math.Max(WorldTerrain.GroundHeightAt(k.Pos.X, k.Pos.Y), FloatHydro.WaterSurfaceAt(k.Pos.X, k.Pos.Y) ?? double.NegativeInfinity);
                r.ClearanceM = Math.Min(r.ClearanceM, -k.Pos.Z - surface);
            }
        }
        r.Pieces = relay.Wreck.Pieces.Count;
        _o.WriteLine($"{scenario}: crash view opened at t {r.OpenedAt:F3} s; {r.Pieces} pieces; fuel sheen {r.SlickRadiusM:F1} m at 60 s");
        _o.WriteLine($"  as it was: camera up to {r.LegacyMaxAboveM:F1} m above the wreck in the last 10 s, {r.LegacyEndAboveM:F1} m at 60 s, range {r.LegacyEndRangeM:F1} m");
        _o.WriteLine($"  now:       camera up to {r.MaxAboveM:F1} m above the wreck in the last 10 s, {r.EndAboveM:F1} m at 60 s, range {r.EndRangeM:F1} m");
        _o.WriteLine($"  now, after 3 s: look {r.MinPitchDeg:F1}…{r.MaxPitchDeg:F1}°, airframe ≤ {r.AirframeOffDeg:F1}° off axis, ≥ {r.ClearanceM:F1} m over the surface");
        return r;
    }

    /// <summary>On the runway, the grass, steep onto the runway, steep into water: the camera ends no higher above the
    /// wreck than half of three chase distances (3 × 16.5 m × sin 30° = 24.75 m on the Pitts), looks DOWN at it, never
    /// up, and keeps the airframe in the frame.</summary>
    [Theory]
    [InlineData("runway")]
    [InlineData("grass")]
    [InlineData("steep")]
    [InlineData("water")]
    public void AfterACrashTheCameraLooksDownAtTheWreckFromAHeightTheWreckSets(string scenario)
    {
        const double maxAboveM = 24.75 + 0.25;
        Run r;
        using (scenario == "water" ? new CrashWaterTests.FlatWater(WaterUp) : null) r = Crash(scenario);
        Assert.False(double.IsNaN(r.OpenedAt), "pieces broke off and the crash view never opened");
        Assert.True(r.OpenedAt < 1.0, $"the crash view opened {r.OpenedAt:F2} s after the first touch");
        Assert.True(r.MaxAboveM < maxAboveM, $"the camera was {r.MaxAboveM:F1} m above the wreck");
        // Looking down, 30° once eased in (the easing swings it ±12°): never level, never up into the sky.
        Assert.InRange(r.MinPitchDeg, -45.0, -15.0);
        Assert.InRange(r.MaxPitchDeg, -45.0, -15.0);
        Assert.True(r.AirframeOffDeg < HalfFovRad * 180 / Math.PI, $"the airframe was {r.AirframeOffDeg:F1}° off the view axis");
        Assert.True(r.ClearanceM > 1.524, $"the camera came within {r.ClearanceM:F2} m of the surface");
    }

    /// <summary>The report itself: into the water the old camera was still climbing a minute later with the fuel sheen,
    /// and the new one sits still.</summary>
    [Fact]
    public void IntoTheWaterTheOldCameraKeptClimbingAndTheNewOneDoesNot()
    {
        Run r;
        using (new CrashWaterTests.FlatWater(WaterUp)) r = Crash("water");
        Assert.True(r.SlickRadiusM > 25, $"the slick was {r.SlickRadiusM:F1} m at 60 s");
        Assert.True(r.LegacyEndAboveM > 100, $"the old camera was only {r.LegacyEndAboveM:F1} m up at 60 s (the bug did not reproduce)");
        Assert.True(r.EndAboveM < r.LegacyEndAboveM / 4, $"now {r.EndAboveM:F1} m up against {r.LegacyEndAboveM:F1} m");
        Assert.True(r.MaxAboveM - r.EndAboveM < 1.0, $"still moving in the last 10 s: {r.MaxAboveM:F1} → {r.EndAboveM:F1} m");
    }

    /// <summary>The water's surface for the water crash: 4 m over the first island runway.</summary>
    private static double WaterUp
    {
        get
        {
            var prev = WorldTerrain.Active; WorldTerrain.Active ??= new WorldTerrain();
            WorldTerrain.RunwayEnd rw = IslandRunways.Ends(WorldTerrain.Airports.First(x => IslandRunways.Ends(x).Count > 0))[0];
            double up = WorldTerrain.GroundHeightAt(rw.CentreX, rw.CentreY) + 4.0;
            WorldTerrain.Active = prev;
            return up;
        }
    }

    /// <summary>The Pitts over the first island runway's centreline, nose <paramref name="noseDownDeg"/> below the horizon,
    /// flying along its nose at <paramref name="speedMs"/>, its lowest point 10 cm over the ground plus
    /// <paramref name="aboveGroundM"/> (the water's depth, for the water crash).</summary>
    private static RigidBodyState Steep(AircraftConfig c, double noseDownDeg, double speedMs, double aboveGroundM)
    {
        WorldTerrain.Airport ap = WorldTerrain.Airports.First(x => IslandRunways.Ends(x).Count > 0);
        WorldTerrain.RunwayEnd rw = IslandRunways.Ends(ap)[0];
        double hdg = rw.HeadingRad, pitch = -noseDownDeg * Math.PI / 180;
        Quat q = Quat.Multiply(new Quat(0, 0, Math.Sin(hdg / 2), Math.Cos(hdg / 2)), new Quat(0, Math.Sin(pitch / 2), 0, Math.Cos(pitch / 2)));
        Vec3 cg = c.Mass.CgVec();
        double low = double.NegativeInfinity;
        foreach (ContactPoint p in AirframeContact.BuildPoints(c)) if (!p.PropDisc) low = Math.Max(low, q.Rotate(p.Body - cg).Z);
        foreach (GearConfig gc in c.Gear) low = Math.Max(low, q.Rotate(gc.PosVec() - cg).Z);
        double ground = WorldTerrain.GroundHeightAt(rw.CentreX, rw.CentreY);
        return new RigidBodyState(new Vec3(rw.CentreX, rw.CentreY, -(ground + aboveGroundM + low + 0.10)), q, new Vec3(speedMs, 0, 0), Vec3.Zero);
    }

    /// <summary>Where the flight-path camera is and looks: ChaseCamera.PlaceCamera on the RelativeWind / ExactFlightPath
    /// branch (behind on the path, raised along world up — or the airframe's up within 26° of vertical — looking 4 m
    /// ahead of the aircraft), then KeepAboveSurface (lifted to 5 ft over the ground or water and turned to the aircraft).</summary>
    private static (Vec3 Cam, Vec3 Look) FlightPathPose(RigidBodyState s, Vec3 dir, double distM, double heightM)
    {
        Vec3 worldUp = new(0, 0, -1);
        Vec3 up = Math.Abs(dir.Dot(worldUp)) > 0.9 ? s.Attitude.Rotate(new Vec3(0, 0, -1)) : worldUp;
        Vec3 cam = s.Position - dir * distM + up * heightM;
        Vec3 look = s.Position + dir * 4 - cam;
        double floor = Math.Max(WorldTerrain.GroundHeightAt(cam.X, cam.Y), FloatHydro.WaterSurfaceAt(cam.X, cam.Y) ?? double.NegativeInfinity) + 1.524;
        if (-cam.Z < floor) { cam = new Vec3(cam.X, cam.Y, -floor); look = s.Position - cam; }
        return (cam, look.Normalized());
    }

    /// <summary>Unity's Vector3.SmoothDamp / Mathf.SmoothDamp with no speed limit.</summary>
    private static Vec3 SmoothDamp(Vec3 cur, Vec3 target, ref Vec3 vel, double smoothS, double dt)
    {
        double omega = 2 / smoothS, x = omega * dt, e = 1 / (1 + x + 0.48 * x * x + 0.235 * x * x * x);
        Vec3 change = cur - target, temp = (vel + change * omega) * dt;
        vel = (vel - temp * omega) * e;
        return target + (change + temp) * e;
    }
    private static double SmoothDamp(double cur, double target, ref double vel, double smoothS, double dt)
    {
        double omega = 2 / smoothS, x = omega * dt, e = 1 / (1 + x + 0.48 * x * x + 0.235 * x * x * x);
        double change = cur - target, temp = (vel + omega * change) * dt;
        vel = (vel - omega * temp) * e;
        return target + (change + temp) * e;
    }
}

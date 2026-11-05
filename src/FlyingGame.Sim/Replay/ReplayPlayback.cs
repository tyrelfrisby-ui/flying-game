using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.Aero.Wake;
using FlyingGame.Core.Combat;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim.Replay;

/// <summary>
/// Where a session is played: a rigid move of the whole session (every track together, so a formation keeps its shape)
/// — a turn about the vertical and a translation. <see cref="Original"/> plays it where it was flown.
/// </summary>
public readonly struct SessionPlacement
{
    /// <summary>Session-space point that is moved onto <see cref="Target"/>.</summary>
    public readonly Vec3 Pivot, Target;
    public readonly double YawRad;
    private readonly Quat _yaw;

    public SessionPlacement(Vec3 pivot, Vec3 target, double yawRad)
    {
        Pivot = pivot; Target = target; YawRad = yawRad;
        _yaw = new Quat(0, 0, System.Math.Sin(yawRad / 2), System.Math.Cos(yawRad / 2));
    }

    public static SessionPlacement Original => new(Vec3.Zero, Vec3.Zero, 0.0);
    public bool IsOriginal => YawRad == 0.0 && (Target - Pivot).Length == 0.0;

    public Vec3 Point(Vec3 p) => Target + Yaw.Rotate(p - Pivot);
    public Vec3 Direction(Vec3 v) => Yaw.Rotate(v);
    private Quat Yaw => _yaw.W == 0 && _yaw.Z == 0 ? Quat.Identity : _yaw;

    /// <summary>A recorded sample as it appears in the world. Body-axis velocity and rates are unchanged by a rigid move.</summary>
    public TrackSample Apply(in TrackSample s)
    {
        RigidBodyState st = s.State;
        var moved = new RigidBodyState(Point(st.Position), Quat.Multiply(Yaw, st.Attitude).Normalized(), st.Velocity, st.Rates);
        return new TrackSample(moved, s.Deflections, s.Throttle01, s.GearExtension, s.EngineRpm, s.LoadFactorZ, Direction(s.WindWorld), s.Flags);
    }

    /// <summary>"Start at my position": the session's reference ship (its pose at <paramref name="reference"/>) is put
    /// <paramref name="aheadM"/> ahead of the live aircraft at its height, on its heading.</summary>
    public static SessionPlacement AtPilot(RigidBodyState reference, RigidBodyState live, double aheadM)
    {
        double refPsi = FormationPilot.Euler(reference.Attitude).psi, livePsi = FormationPilot.Euler(live.Attitude).psi;
        var fwd = new Vec3(System.Math.Cos(livePsi), System.Math.Sin(livePsi), 0);
        return new SessionPlacement(reference.Position, live.Position + fwd * aheadM, livePsi - refPsi);
    }
}

public enum PlacementMode { Original, AtMyPosition }

/// <summary>How a session is played (Ty's options: loop, start offset, start at my position or original location,
/// several at once).</summary>
public sealed class PlaybackOptions
{
    public bool Loop;
    /// <summary>Session time to begin at (s).</summary>
    public double StartOffsetSec;
    public PlacementMode Placement = PlacementMode.Original;
    /// <summary>For <see cref="PlacementMode.AtMyPosition"/>: how far ahead of the pilot the lead appears (m).</summary>
    public double AheadM = 150.0;
    /// <summary>A ship shot down comes back whole when the loop starts again.</summary>
    public bool RespawnOnLoop = true;
    /// <summary>The live aircraft can collide with the recorded ships (on by default; recorded-with-recorded is the
    /// session's <see cref="FlightSession.CollideRecordedShips"/>).</summary>
    public bool CollideWithLive = true;
    /// <summary>Bullets can hit the recorded ships.</summary>
    public bool Targets = true;
}

public enum ActorMode
{
    /// <summary>Before its track begins or after it ends (not drawn, not a target).</summary>
    Absent,
    /// <summary>Following its recording exactly.</summary>
    Kinematic,
    /// <summary>Shot down (or flown into the ground somewhere it was not recorded): the physics has it now.</summary>
    Wreck,
    /// <summary>A wreck on the ground: not stepped any more (a resting wreck costs 4x a flying aircraft; see CombatController).</summary>
    Settled,
}

/// <summary>
/// One recorded ship in the world: a real sim <see cref="Aircraft"/> put into its recorded pose every frame (so the
/// visual, the gauges, the hit volumes and the damage model are the same code as a live aeroplane's), released to
/// the physics with its recorded momentum the moment it is shot down.
/// </summary>
public sealed class ReplayActor
{
    public readonly int Id;
    public readonly FlightTrack Track;
    public readonly AircraftConfig Config;
    /// <summary>The track's aircraft id when this actor was built: a track re-typed since is rebuilt by
    /// <see cref="SessionPlayback.Refresh"/>.</summary>
    public readonly string BuiltAircraftId;
    public Aircraft Aircraft { get; private set; }
    public ActorMode Mode { get; private set; } = ActorMode.Absent;
    public GunTarget Target { get; private set; }
    public IReadOnlyList<HitVolume> Volumes { get; }
    /// <summary>The sample the actor was last put into (world placement applied).</summary>
    public TrackSample Current { get; private set; }
    /// <summary>Bumped whenever <see cref="Aircraft"/> is replaced (loop respawn, seek back past a lost part): the
    /// visual must rebuild its parts.</summary>
    public int Generation { get; private set; }
    /// <summary>Smoke colour now (0xRRGGBB).</summary>
    public int SmokeRgb { get; private set; }
    public bool ShotDown { get; private set; }
    public double WreckSec { get; private set; }

    private int _eventCursor;
    private int _lastFrame = -1;
    private double _lastFollowSec = double.NaN;

    /// <summary>Recorded ships shed wakes (docs/WAKE-VORTEX.md) when the host turns this on; off in every test that
    /// does not ask for it.</summary>
    public static bool ShedsWakes { get; set; }

    public ReplayActor(int id, FlightTrack track, AircraftConfig config)
    {
        Id = id; Track = track; Config = config; BuiltAircraftId = track.AircraftId;
        Volumes = HitVolumes.Build(config);
        SmokeRgb = track.SmokeRgb;
        Aircraft = null!; Target = null!;
        Respawn();
    }

    /// <summary>A whole, undamaged aircraft at the track's first frame.</summary>
    public void Respawn()
    {
        TrackSample s0 = Track.FrameCount > 0 ? Track.Frame(0) : default;
        Aircraft = new Aircraft(Config, s0.State, s0.Deflections);
        if (Config.RetractableGear) Aircraft.SetGear(s0.GearExtension > 0.5, immediate: true);
        Aircraft a = Aircraft;
        Target = new GunTarget { Id = Id, Config = Config, Volumes = Volumes, State = () => a.State, Lost = a.IsLost, Damage = a.Damage };
        Mode = ActorMode.Absent; ShotDown = false; WreckSec = 0;
        _eventCursor = 0; _lastFrame = -1; SmokeRgb = Track.SmokeRgb;
        Generation++;
    }

    /// <summary>Same rule as the combat drones: an inner wing panel or the tail boom gone and it cannot fly.</summary>
    public static bool Crippled(Aircraft a) =>
        a.IsLost(AirframeComponent.WingLeftInner) || a.IsLost(AirframeComponent.WingRightInner) || a.IsLost(AirframeComponent.TailBoom)
        || a.IsLost(AirframeComponent.WingLeft) || a.IsLost(AirframeComponent.WingRight);

    /// <summary>Put the actor at session time <paramref name="sessionSec"/>. Kinematic only; a wreck ignores the clock.</summary>
    public void Follow(double sessionSec, in SessionPlacement placement, bool groundCheck)
    {
        if (Mode is ActorMode.Wreck or ActorMode.Settled) return;
        bool inside = Track.TrySampleSession(sessionSec, out TrackSample raw);
        if (!inside) { Mode = ActorMode.Absent; return; }
        double local = sessionSec - Track.BeginSec;
        int frame = Track.FrameAt(local);
        if (frame < _lastFrame && HasLossesUpTo(_lastFrame)) Respawn();   // seeking back past a lost part: start whole
        else if (frame < _lastFrame) { _eventCursor = 0; SmokeRgb = Track.SmokeRgb; }
        ReplayEvents(frame);
        _lastFrame = frame;

        TrackSample s = placement.Apply(raw);
        Current = s;
        Aircraft.SetReplayPose(s.State, s.Deflections, s.Throttle01, s.Deflections.FlapFraction, s.GearExtension, s.LoadFactorZ);
        Mode = ActorMode.Kinematic;
        if (ShedsWakes)
        {
            Aircraft.Wake ??= VortexWake.CreateSource($"replay-{Id}", AeroModel.WingSpan(Config));
            double dtF = double.IsNaN(_lastFollowSec) ? 0.0 : sessionSec - _lastFollowSec;
            if (dtF > 0.0 && dtF < 0.5) Aircraft.ShedPoseWake(dtF);
        }
        _lastFollowSec = sessionSec;
        if (Crippled(Aircraft)) { ShotDown = true; Release(); return; }
        // Moved somewhere it was not flown, the recording can pass through a hill. It crashes into it, as a live
        // aeroplane would, rather than flying through the island.
        if (groundCheck && !s.Has(RecordFlags.OnGround))
        {
            double agl = -s.State.Position.Z - WorldTerrain.GroundHeightAt(s.State.Position.X, s.State.Position.Y);
            if (agl < 0.5) Release();
        }
    }

    private bool HasLossesUpTo(int frame)
    {
        if (Aircraft.LostComponents.Count > 0) return true;
        foreach (TrackEvent e in Track.Events) { if (e.Frame > frame) break; if (e.Kind == TrackEventKind.PartLost) return true; }
        return false;
    }

    private void ReplayEvents(int frame)
    {
        while (_eventCursor < Track.Events.Count && Track.Events[_eventCursor].Frame <= frame)
        {
            TrackEvent e = Track.Events[_eventCursor++];
            if (e.Kind == TrackEventKind.PartLost)
            {
                var c = (AirframeComponent)e.Arg;
                if (!Aircraft.IsLost(c)) Aircraft.LoseComponent(c);
            }
            else if (e.Kind == TrackEventKind.SmokeColour) SmokeRgb = e.Arg;
        }
    }

    /// <summary>Take a following ship out of the world (its session has ended).</summary>
    public void Hide() { if (Mode == ActorMode.Kinematic) Mode = ActorMode.Absent; }

    /// <summary>Hand the aircraft to the physics as it is: recorded velocity and rates, whatever is left of it.</summary>
    public void Release()
    {
        if (Mode is ActorMode.Wreck or ActorMode.Settled) return;
        Mode = ActorMode.Wreck; WreckSec = 0;
    }

    /// <summary>Fly the wreck one step: controls released, power off. It settles once down (as a drone does).</summary>
    public void StepWreck(double dt)
    {
        if (Mode != ActorMode.Wreck) return;
        new SimLoop(Aircraft, dt).RunFor(dt, new ControlInputs(0, 0, 0, 1.0, true, true, true));
        WreckSec += dt;
        RigidBodyState st = Aircraft.State;
        double agl = -st.Position.Z - WorldTerrain.GroundHeightAt(st.Position.X, st.Position.Y);
        if ((agl < 2.0 && WreckSec > 0.5) || (WreckSec > 1.0 && st.Velocity.Length < 2.0) || WreckSec > 60.0) Mode = ActorMode.Settled;
    }

    /// <summary>World position of a body point given in the config frame (the frame of the hit volumes and contact points).</summary>
    public Vec3 WorldPoint(Vec3 body) => Aircraft.State.Position + Aircraft.State.Attitude.Rotate(body - Config.Mass.CgVec());
}

/// <summary>One session being played: its clock, its placement and its ships.</summary>
public sealed class SessionPlayback
{
    public readonly FlightSession Session;
    public readonly PlaybackOptions Options;
    public SessionPlacement Placement { get; private set; }
    public readonly List<ReplayActor> Actors = new();
    /// <summary>Session time now (s).</summary>
    public double Clock { get; private set; }
    public bool Paused;
    public bool Finished { get; private set; }
    public int Loops { get; private set; }
    public int Hits, Kills;
    /// <summary>A track that is not drawn here (the one being re-flown in a take).</summary>
    public FlightTrack? Excluded { get; private set; }
    /// <summary>The take this playback accompanies, or null. While set, the playback's clock IS the take's clock: the
    /// other ships hold at the punch-in through the count-in and run with the take after GO, so a new track is flown
    /// against exactly what will play beside it. No loop and no end while it records.</summary>
    public RecordingTake? Take { get; private set; }

    private readonly ReplayDirector _director;
    private int _revision;

    internal SessionPlayback(ReplayDirector director, FlightSession session, PlaybackOptions options, SessionPlacement placement,
        FlightTrack? excluded, RecordingTake? take)
    {
        _director = director;
        Session = session; Options = options; Placement = placement; Excluded = excluded; Take = take;
        Clock = take?.Clock ?? options.StartOffsetSec;
        Refresh();
    }

    /// <summary>Match the ships to the session's tracks after an edit while playing: a deleted track's ship goes, an
    /// added or committed track gets one (an overdub's new take replaces the old ship), a re-typed track is rebuilt.
    /// <see cref="Advance"/> calls it whenever <see cref="FlightSession.Revision"/> has moved.</summary>
    public void Refresh()
    {
        _revision = Session.Revision;
        Actors.RemoveAll(a => a.Track == Excluded || a.Track.FrameCount == 0 || a.BuiltAircraftId != a.Track.AircraftId
            || !Session.Tracks.Contains(a.Track));
        foreach (FlightTrack t in Session.Tracks)
        {
            if (t.FrameCount == 0 || t == Excluded || Actors.Exists(a => a.Track == t)) continue;
            AircraftConfig? cfg = _director.ConfigFor(t.AircraftId);
            if (cfg == null) continue;   // an aeroplane this build no longer has: that ship is left out, the rest still play
            Actors.Add(new ReplayActor(_director.NextActorId(), t, cfg));
        }
        // Keep session order, so ship n is always drawn and listed as ship n.
        Actors.Sort((a, b) => Session.Tracks.IndexOf(a.Track).CompareTo(Session.Tracks.IndexOf(b.Track)));
    }

    /// <summary>The take is over: let the playback run on its own clock again (from where the take stopped), with the
    /// re-flown track back in it.</summary>
    public void ReleaseTake()
    {
        if (Take == null) return;
        Clock = Take.Clock; Take = null; Excluded = null; Finished = false;
        Refresh();
    }

    public void Seek(double sessionSec) { Clock = sessionSec; Finished = false; }

    /// <summary>Where a live pilot joining this playback as one more ship should be, in the WORLD: slot
    /// <paramref name="slotBody"/> (lead body axes, m) on the ship leading now, with the playback's placement applied
    /// (so "ahead of me" sessions are joined where they are drawn). Null when no ship is flying or the playback has ended.</summary>
    public RigidBodyState? JoinPose(Vec3 slotBody)
    {
        if (Finished) return null;
        FlightTrack? lead = Session.LeadAt(Clock, Excluded);
        if (lead == null) return null;
        lead.TrySampleSession(Clock, out TrackSample s);
        RigidBodyState l = Placement.Apply(s).State;
        return new RigidBodyState(l.Position + l.Attitude.Rotate(slotBody), l.Attitude, l.Velocity, l.Rates);
    }

    /// <summary>Advance the clock (unless paused) and put every playing ship in place.</summary>
    public void Advance(double dt)
    {
        if (Session.Revision != _revision) Refresh();
        if (Take != null) { Clock = Take.Clock; Finished = false; }
        else if (!Paused && !Finished) Clock += dt;
        double end = Session.Duration;
        if (Take == null && Clock > end)
        {
            if (Options.Loop && end > 0)
            {
                Clock = Options.StartOffsetSec + (Clock - end);
                if (Clock > end || Clock < 0) Clock = 0;
                Loops++;
                foreach (ReplayActor a in Actors) if (Options.RespawnOnLoop || a.Mode is ActorMode.Kinematic or ActorMode.Absent) a.Respawn();
            }
            else { Clock = end; Finished = true; }
        }
        bool groundCheck = !Placement.IsOriginal;
        foreach (ReplayActor a in Actors)
        {
            if (!Session.Plays(a.Track)) { if (a.Mode == ActorMode.Kinematic) a.Respawn(); continue; }
            // A burning panel burns through on the clock whether or not the physics has the aeroplane.
            if (a.Mode == ActorMode.Kinematic && !Paused) a.Aircraft.Damage.Update(dt);
            bool was = a.ShotDown;
            a.Follow(Clock, Placement, groundCheck);
            if (a.ShotDown && !was) Kills++;
            if (Finished) a.Hide();   // the session is over: its ships go (a wreck stays where it fell)
        }
    }

    /// <summary>Ships that are in the world now (drawn, can be hit).</summary>
    public IEnumerable<ReplayActor> Present()
    {
        foreach (ReplayActor a in Actors) if (a.Mode != ActorMode.Absent && Session.Plays(a.Track)) yield return a;
    }
}

/// <summary>A collision found between two aircraft (the live one, or two recorded ships).</summary>
public readonly struct ReplayCollision
{
    public readonly ReplayActor Actor; public readonly ReplayActor? Other; public readonly AirframeComponent ActorPart;
    public readonly AirframeComponent OtherPart; public readonly Vec3 Pos;
    public ReplayCollision(ReplayActor actor, ReplayActor? other, AirframeComponent actorPart, AirframeComponent otherPart, Vec3 pos)
    { Actor = actor; Other = other; ActorPart = actorPart; OtherPart = otherPart; Pos = pos; }
}

/// <summary>
/// Everything recorded that is playing (Ty: "multiple at once"): any number of sessions, each with its own clock and
/// placement. Engine-free: the Bridge calls <see cref="Tick"/> once a frame, draws <see cref="Present"/>, hands
/// <see cref="GunTargets"/> to the Gunnery and reports its hits back through <see cref="OnHit"/>.
/// </summary>
public sealed class ReplayDirector
{
    /// <summary>Gunnery target ids for recorded ships start here (player 0, drones 100+, dogfight 999).</summary>
    public const int FirstActorId = 1000;
    public readonly List<SessionPlayback> Playing = new();
    internal readonly Func<string, AircraftConfig?> ConfigFor;
    private int _nextId = FirstActorId;
    private double _wreckAccum;
    private readonly List<GunTarget> _targets = new();

    public ReplayDirector(Func<string, AircraftConfig?> configFor) { ConfigFor = configFor; }

    internal int NextActorId() => _nextId++;

    /// <summary>Start playing <paramref name="session"/>. <paramref name="live"/> is the pilot (for "at my position").</summary>
    public SessionPlayback Play(FlightSession session, PlaybackOptions options, RigidBodyState? live = null, FlightTrack? exclude = null)
    {
        SessionPlacement placement = SessionPlacement.Original;
        if (options.Placement == PlacementMode.AtMyPosition && live.HasValue)
        {
            FlightTrack? lead = session.LeadAt(options.StartOffsetSec, exclude) ?? FirstPlaying(session, exclude);
            if (lead != null)
            {
                lead.TrySampleSession(options.StartOffsetSec, out TrackSample s);
                placement = SessionPlacement.AtPilot(s.State, live.Value, options.AheadM);
            }
        }
        var p = new SessionPlayback(this, session, options, placement, exclude, null);
        Playing.Add(p);
        p.Advance(0);
        return p;
    }

    /// <summary>Play the rest of a take's session alongside it, on the take's clock (Ty: "records track 2 while track 1
    /// plays back synced from a common start cue/countdown"). It plays where it was flown and leaves out the track an
    /// overdub is re-flying. Tick the take before the director each frame. When the take ends, either
    /// <see cref="Stop"/> it or <see cref="SessionPlayback.ReleaseTake"/> to keep playing with the new track in.</summary>
    public SessionPlayback PlayAlong(RecordingTake take, PlaybackOptions? options = null)
    {
        var p = new SessionPlayback(this, take.Session, options ?? new PlaybackOptions(), SessionPlacement.Original, take.Replacing, take);
        Playing.Add(p);
        p.Advance(0);
        return p;
    }

    private static FlightTrack? FirstPlaying(FlightSession s, FlightTrack? except)
    {
        foreach (FlightTrack t in s.Tracks) if (t != except && s.Plays(t) && t.FrameCount > 0) return t;
        return null;
    }

    public void Stop(SessionPlayback p) => Playing.Remove(p);
    public void StopAll() => Playing.Clear();

    /// <summary>One display frame: clocks, poses, and the wrecks at the AI rate.</summary>
    public void Tick(double dt)
    {
        foreach (SessionPlayback p in Playing) p.Advance(dt);
        _wreckAccum = System.Math.Min(_wreckAccum + dt, SimLoop.MaxCatchUpSec);
        while (_wreckAccum >= SimLoop.AiFixedDtSec)
        {
            foreach (SessionPlayback p in Playing) foreach (ReplayActor a in p.Actors) a.StepWreck(SimLoop.AiFixedDtSec);
            _wreckAccum -= SimLoop.AiFixedDtSec;
        }
    }

    public IEnumerable<ReplayActor> Present()
    {
        foreach (SessionPlayback p in Playing) foreach (ReplayActor a in p.Present()) yield return a;
    }

    /// <summary>Recorded ships bullets can hit now. A settled wreck is no target.</summary>
    public IReadOnlyList<GunTarget> GunTargets()
    {
        _targets.Clear();
        foreach (SessionPlayback p in Playing)
        {
            if (!p.Options.Targets) continue;
            foreach (ReplayActor a in p.Present()) if (a.Mode != ActorMode.Settled) _targets.Add(a.Target);
        }
        return _targets;
    }

    /// <summary>Score a Gunnery hit (the damage itself was already registered by the Gunnery on the actor's
    /// DamageState). Returns the actor hit, or null if the hit was not on a recorded ship.</summary>
    public ReplayActor? OnHit(in HitEvent hit)
    {
        if (hit.Target < FirstActorId) return null;
        foreach (SessionPlayback p in Playing)
            foreach (ReplayActor a in p.Actors)
            {
                if (a.Id != hit.Target) continue;
                p.Hits++;
                return a;
            }
        return null;
    }

    /// <summary>
    /// Contacts between the live aircraft's hard points and the recorded ships' hit volumes (and, when the session asks
    /// for it, between recorded ships). Broad phase on the distance between CGs. Finding a contact does nothing by
    /// itself: the caller decides what it costs (docs/FLIGHT-RECORDER.md, "Collisions").
    /// </summary>
    public List<ReplayCollision> Collisions(Aircraft? live)
    {
        var hits = new List<ReplayCollision>();
        foreach (SessionPlayback p in Playing)
        {
            var present = new List<ReplayActor>(p.Present());
            if (live != null && p.Options.CollideWithLive)
            {
                Vec3 lcg = live.Config.Mass.CgVec();
                foreach (ReplayActor a in present)
                {
                    if ((a.Aircraft.State.Position - live.State.Position).Length > 40) continue;
                    foreach (ContactPoint cp in live.ContactPoints)
                    {
                        Vec3 w = live.State.Position + live.State.Attitude.Rotate(cp.Body - lcg);
                        AirframeComponent? part = PointIn(a, w);
                        if (part.HasValue) { hits.Add(new ReplayCollision(a, null, part.Value, cp.Component, w)); break; }
                    }
                }
            }
            if (!p.Session.CollideRecordedShips) continue;
            for (int i = 0; i < present.Count; i++)
                for (int j = i + 1; j < present.Count; j++)
                {
                    ReplayActor a = present[i], b = present[j];
                    if ((a.Aircraft.State.Position - b.Aircraft.State.Position).Length > 40) continue;
                    foreach (ContactPoint cp in b.Aircraft.ContactPoints)
                    {
                        Vec3 w = b.WorldPoint(cp.Body);
                        AirframeComponent? part = PointIn(a, w);
                        if (part.HasValue) { hits.Add(new ReplayCollision(a, b, part.Value, cp.Component, w)); break; }
                    }
                }
        }
        return hits;
    }

    /// <summary>The actor's part whose hit volume contains world point <paramref name="w"/>, or null.</summary>
    public static AirframeComponent? PointIn(ReplayActor a, Vec3 w)
    {
        RigidBodyState s = a.Aircraft.State;
        Vec3 b = s.Attitude.Conjugate().Rotate(w - s.Position) + a.Config.Mass.CgVec();
        foreach (HitVolume hv in a.Volumes)
        {
            if (a.Aircraft.IsLost(hv.Part)) continue;
            Vec3 d = b - hv.Centre;
            if (System.Math.Abs(d.X) <= hv.Half.X && System.Math.Abs(d.Y) <= hv.Half.Y && System.Math.Abs(d.Z) <= hv.Half.Z) return hv.Part;
        }
        return null;
    }
}

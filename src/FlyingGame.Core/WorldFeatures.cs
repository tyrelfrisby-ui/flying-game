using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>Marked IAC aerobatic box beside every runway (its west edge 500 m east of the centreline):
/// 1,000 m square, floor 328 ft, ceiling 3,500 ft — drawn as a glass box in the sky. The constants are the
/// Valley box; <see cref="CenterYAt"/> gives the copy on plateau p.</summary>
public static class AeroBox
{
    public const double CenterX = 400, CenterY = 650, SizeM = 1000.0;   // owner 2026-10-05: right next to the runway
    public const double FloorAglM = 100.0, CeilingAglM = 1067.0;
    public static double CenterYAt(int p) => CenterY + WorldTerrain.PlateauDy(p);
    public static bool Inside(Vec3 pos)
    {
        double agl = -pos.Z - WorldTerrain.GroundHeightAt(pos.X, pos.Y);
        if (System.Math.Abs(pos.X - CenterX) > SizeM / 2 || agl < FloorAglM || agl > CeilingAglM) return false;
        for (int p = 0; p < WorldTerrain.PlateauCount; p++)
            if (System.Math.Abs(pos.Y - CenterYAt(p)) <= SizeM / 2) return true;
        return false;
    }
}

/// <summary>One element of the air-racing course: an air gate (two pylons, fly between, level, below the
/// top) or a single turning pylon that must be kept on the stated side.</summary>
public sealed class RaceElement
{
    public enum Kinds { Gate, PylonOnLeft, PylonOnRight }
    public Kinds Kind;
    public double X, Y, HeadingDeg;      // position; heading = required direction of travel through it
    public const double GateHalfWidthM = 22.86, GateHeightM = 75.0, PylonRadiusM = 1.5;   // pylons 150 ft apart (owner 2026-10-06: "farther apart"; was 100 ft), 75 m tall
    public const double NumberAglM = 91.44;                                                // the rotating numbers, 300 ft up

    public Vec3 Forward => new(System.Math.Cos(HeadingDeg * System.Math.PI / 180), System.Math.Sin(HeadingDeg * System.Math.PI / 180), 0);
    public Vec3 Right => new(-System.Math.Sin(HeadingDeg * System.Math.PI / 180), System.Math.Cos(HeadingDeg * System.Math.PI / 180), 0);
    public Vec3 Centre => new(X, Y, 0);
}

/// <summary>"Air Racing" (owner request; no brand names): a COMPACT track east of the Valley runway, laid out the
/// way the pylon-racing world championship tracks are — start gate, a gate, a three-pylon chicane, a gate, a
/// vertical turning pylon at the far end, then back through two gates and a chicane to a separate finish gate.
/// About 1,100 × 350 m (owner 2026-10-06: stretched 1.5× from the turning pylon, the chicane weave too). Elements are taken IN ORDER; the rotating numbers 300 ft above each element show the way.
/// <see cref="Elements"/> is the Valley course; <see cref="ElementsFor"/> the identical copy on plateau p.</summary>
public static class RaceCourse
{
    public static readonly RaceElement[] Elements =
    {
        new() { Kind = RaceElement.Kinds.Gate, X = 45, Y = 1800, HeadingDeg = 0 },          // 1 START, northbound
        new() { Kind = RaceElement.Kinds.Gate, X = 345, Y = 1800, HeadingDeg = 0 },         // 2
        new() { Kind = RaceElement.Kinds.PylonOnRight, X = 540, Y = 1755, HeadingDeg = 0 }, // 3 chicane
        new() { Kind = RaceElement.Kinds.PylonOnLeft, X = 720, Y = 1845, HeadingDeg = 0 },  // 4
        new() { Kind = RaceElement.Kinds.PylonOnRight, X = 900, Y = 1755, HeadingDeg = 0 }, // 5
        new() { Kind = RaceElement.Kinds.Gate, X = 1050, Y = 1800, HeadingDeg = 0 },         // 6
        new() { Kind = RaceElement.Kinds.PylonOnRight, X = 1110, Y = 1920, HeadingDeg = 90 },// 7 vertical turning pylon: around it and back south
        new() { Kind = RaceElement.Kinds.Gate, X = 1020, Y = 2050, HeadingDeg = 180 },       // 8 southbound
        new() { Kind = RaceElement.Kinds.Gate, X = 720, Y = 2050, HeadingDeg = 180 },       // 9
        new() { Kind = RaceElement.Kinds.PylonOnLeft, X = 540, Y = 2095, HeadingDeg = 180 },// 10 chicane
        new() { Kind = RaceElement.Kinds.PylonOnRight, X = 360, Y = 2005, HeadingDeg = 180 },// 11
        new() { Kind = RaceElement.Kinds.Gate, X = 120, Y = 2050, HeadingDeg = 180 },        // 12 FINISH
    };

    private static readonly Dictionary<int, RaceElement[]> _byPlateau = new();

    /// <summary>The course on plateau <paramref name="p"/>: the Valley elements shifted by <see cref="WorldTerrain.PlateauDy"/>.</summary>
    public static RaceElement[] ElementsFor(int p)
    {
        if (p == 0) return Elements;
        lock (_byPlateau)
        {
            if (_byPlateau.TryGetValue(p, out RaceElement[]? cached)) return cached;
            double dy = WorldTerrain.PlateauDy(p);
            var arr = new RaceElement[Elements.Length];
            for (int i = 0; i < arr.Length; i++) arr[i] = new RaceElement { Kind = Elements[i].Kind, X = Elements[i].X, Y = Elements[i].Y + dy, HeadingDeg = Elements[i].HeadingDeg };
            _byPlateau[p] = arr;
            return arr;
        }
    }

    /// <summary>Every pylon on the Valley course: element index, side (−1 left / +1 right of a gate, 0 for a single pylon)
    /// and its base position (x, y).</summary>
    public static List<(int element, int side, double x, double y)> Pylons() => PylonsOf(Elements);

    /// <summary>Every pylon on the plateau-<paramref name="p"/> course.</summary>
    public static List<(int element, int side, double x, double y)> Pylons(int p) => PylonsOf(ElementsFor(p));

    public static List<(int element, int side, double x, double y)> PylonsOf(RaceElement[] elements)
    {
        var list = new List<(int, int, double, double)>();
        for (int i = 0; i < elements.Length; i++)
        {
            RaceElement e = elements[i];
            if (e.Kind == RaceElement.Kinds.Gate)
            {
                Vec3 r = e.Right;
                list.Add((i, -1, e.X - r.X * RaceElement.GateHalfWidthM, e.Y - r.Y * RaceElement.GateHalfWidthM));
                list.Add((i, +1, e.X + r.X * RaceElement.GateHalfWidthM, e.Y + r.Y * RaceElement.GateHalfWidthM));
            }
            else list.Add((i, 0, e.X, e.Y));
        }
        return list;
    }
}

/// <summary>Race scoring: sequence, lap timer, penalties. Feed it the aircraft position each step.</summary>
public sealed class AirRace
{
    public int Next { get; private set; }            // index of the element to take next
    public bool Running { get; private set; }
    public bool Finished { get; private set; }
    public double ElapsedSec { get; private set; }
    public double PenaltySec { get; private set; }
    public string LastEvent { get; private set; } = "Cross the start gate";
    public double TotalSec => ElapsedSec + PenaltySec;

    private Vec3 _prev; private bool _havePrev;
    private readonly RaceElement[] _els;
    private readonly List<(int element, int side, double x, double y)> _pylons;
    private readonly Dictionary<(int, int), double> _strikeCooldown = new();
    public const double PylonStrikeCooldownSec = 4.0;

    /// <summary>Wing-tip-to-wing-tip line against every pylon (2-D, below the pylon top): the first pylon it cuts
    /// through is struck. Returns (element, side) once per pylon per few seconds; +3 s while racing.</summary>
    public (int element, int side)? CheckPylonStrike(Vec3 leftTip, Vec3 rightTip, double dt)
    {
        foreach (var k in new List<(int, int)>(_strikeCooldown.Keys)) { _strikeCooldown[k] -= dt; if (_strikeCooldown[k] <= 0) _strikeCooldown.Remove(k); }
        foreach ((int el, int side, double px, double py) in _pylons)
        {
            // Closest point on the tip-to-tip segment to the pylon axis, in plan.
            double ax = leftTip.X - px, ay = leftTip.Y - py, bx = rightTip.X - px, by = rightTip.Y - py;
            double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
            double u = len2 > 1e-9 ? System.Math.Clamp(-(ax * dx + ay * dy) / len2, 0.0, 1.0) : 0.0;
            double cx = ax + dx * u, cy = ay + dy * u;
            if (cx * cx + cy * cy > (RaceElement.PylonRadiusM + 0.6) * (RaceElement.PylonRadiusM + 0.6)) continue;
            double z = leftTip.Z + (rightTip.Z - leftTip.Z) * u;
            double agl = -z - WorldTerrain.GroundHeightAt(px, py);
            if (agl < -1 || agl > RaceElement.GateHeightM + 2) continue;
            if (_strikeCooldown.ContainsKey((el, side))) continue;
            _strikeCooldown[(el, side)] = PylonStrikeCooldownSec;
            if (Running && !Finished) { PenaltySec += 3; LastEvent = $"PYLON HIT at {el + 1} +3 s"; }
            else LastEvent = $"Pylon {el + 1} hit";
            return (el, side);
        }
        return null;
    }

    public AirRace() : this(RaceCourse.Elements) { }
    /// <summary>The course on plateau <paramref name="plateau"/>.</summary>
    public AirRace(int plateau) : this(RaceCourse.ElementsFor(plateau)) { }
    public AirRace(RaceElement[] elements) { _els = elements; _pylons = RaceCourse.PylonsOf(elements); }

    public void Reset() { Next = 0; Running = false; Finished = false; ElapsedSec = 0; PenaltySec = 0; _havePrev = false; LastEvent = "Cross the start gate"; }

    /// <param name="bankRad">bank angle (for the level-through-the-gate rule)</param>
    public void Update(Vec3 pos, double bankRad, double dt)
    {
        if (Finished) return;
        if (Running) ElapsedSec += dt;
        if (!_havePrev) { _prev = pos; _havePrev = true; return; }

        RaceElement e = _els[Next];
        Vec3 d = e.Forward, r = e.Right, c = e.Centre;
        double s0 = Vec3.Dot(_prev - c, d), s1 = Vec3.Dot(pos - c, d);
        if (s0 < 0 && s1 >= 0)
        {
            double t = -s0 / (s1 - s0);
            Vec3 q = _prev + (pos - _prev) * t;
            double lateral = Vec3.Dot(q - c, r);
            double agl = -q.Z - WorldTerrain.GroundHeightAt(q.X, q.Y);
            bool near = System.Math.Abs(lateral) < 120 && agl < 200; // only judge if the crossing is actually at the element
            if (near)
            {
                switch (e.Kind)
                {
                    case RaceElement.Kinds.Gate:
                        if (System.Math.Abs(lateral) > RaceElement.GateHalfWidthM || agl > RaceElement.GateHeightM)
                        { PenaltySec += 5; LastEvent = $"Gate {Next + 1} missed +5 s"; }
                        else if (System.Math.Abs(lateral) > RaceElement.GateHalfWidthM - RaceElement.PylonRadiusM - 1.0)
                        { PenaltySec += 3; LastEvent = $"Pylon hit at gate {Next + 1} +3 s"; }
                        else if (System.Math.Abs(bankRad) > 20 * System.Math.PI / 180)
                        { PenaltySec += 2; LastEvent = $"Gate {Next + 1} not level +2 s"; }
                        else LastEvent = $"Gate {Next + 1} clean";
                        break;
                    case RaceElement.Kinds.PylonOnLeft:   // pylon must be on the pilot's LEFT: pass to its right (lateral > 0)
                        if (lateral <= 0) { PenaltySec += 5; LastEvent = $"Pylon {Next + 1} wrong side +5 s"; }
                        else if (lateral < RaceElement.PylonRadiusM + 2) { PenaltySec += 3; LastEvent = $"Pylon {Next + 1} hit +3 s"; }
                        else LastEvent = $"Pylon {Next + 1} ok";
                        break;
                    case RaceElement.Kinds.PylonOnRight:
                        if (lateral >= 0) { PenaltySec += 5; LastEvent = $"Pylon {Next + 1} wrong side +5 s"; }
                        else if (-lateral < RaceElement.PylonRadiusM + 2) { PenaltySec += 3; LastEvent = $"Pylon {Next + 1} hit +3 s"; }
                        else LastEvent = $"Pylon {Next + 1} ok";
                        break;
                }
                if (Next == 0 && !Running) { Running = true; ElapsedSec = 0; PenaltySec = 0; LastEvent = "GO"; }
                if (Next == _els.Length - 1) { Finished = true; Running = false; LastEvent = $"FINISH {TotalSec:F1} s"; }
                if (!Finished) Next++;
            }
        }
        _prev = pos;
    }
}

/// <summary>STOL contest on a gravel strip: land AT or AFTER the line, stop short. Score = stop distance past the
/// line (shorter is better); touching down before the line, or leaving the strip, disqualifies.</summary>
public sealed class StolRun
{
    public enum Phases { Approach, Rolling, Stopped, Disqualified }
    public Phases Phase { get; private set; }
    public double TouchdownPastLineM { get; private set; }
    public double StopPastLineM { get; private set; }
    public string Reason { get; private set; } = "";

    private readonly double _lineX, _stripY, _stripHalfW, _stripEndX;
    private Vec3? _prevPos;

    /// <param name="lineX">landing line x (strip runs along +x)</param>
    public StolRun(double lineX, double stripY, double stripHalfWidth, double stripEndX)
    { _lineX = lineX; _stripY = stripY; _stripHalfW = stripHalfWidth; _stripEndX = stripEndX; }

    public static StolRun ForAirport(WorldTerrain.Airport a)
    {
        WorldTerrain.Strip dirt = System.Array.Find(WorldTerrain.AirportStrips, s => s.Kind == "gravel");
        double south = a.X + dirt.Dx - dirt.Length / 2;
        return new StolRun(south + WorldTerrain.StolLineFromThresholdM, a.Y + dirt.Dy, dirt.Width / 2 + 2, south + dirt.Length);
    }

    public void Update(Vec3 pos, bool mainWheelsOnGround, double groundSpeed)
    {
        if (Phase is Phases.Stopped or Phases.Disqualified) return;
        if (Phase == Phases.Approach && mainWheelsOnGround)
        {
            TouchdownPastLineM = pos.X - _lineX;
            if (System.Math.Abs(pos.Y - _stripY) > _stripHalfW) { Phase = Phases.Disqualified; Reason = "off the strip"; return; }
            if (TouchdownPastLineM < 0) { Phase = Phases.Disqualified; Reason = $"landed {-TouchdownPastLineM * 3.28084:F0} ft SHORT of the line"; return; }
            Phase = Phases.Rolling;
        }
        else if (Phase == Phases.Rolling)
        {
            if (System.Math.Abs(pos.Y - _stripY) > _stripHalfW || pos.X > _stripEndX) { Phase = Phases.Disqualified; Reason = "ran off the strip"; return; }
            if (groundSpeed < 0.4 && mainWheelsOnGround) { StopPastLineM = pos.X - _lineX; Phase = Phases.Stopped; }
        }
        _prevPos = pos;
    }
}


/// <summary>
/// STOL contest (owner 2026-10-07): three landings and three takeoffs on the GRASS strip, alternating, each measured from a
/// white line painted across it (flags either side, judges standing at it, thinner white lines every 10 ft down the
/// distance after it). Landing: on final at 1.1 Vs with full flaps, touch down at or past the line and stop — the distance
/// line → stop point (main wheels). Takeoff: start at rest with the mains on the line — the distance line → lift-off point
/// (the last touch before a full second airborne). Score = average takeoff + average landing (ft); shortest wins. Touching
/// down short of the line or leaving the strip is a foul: that attempt is flown again. Both run southbound (the northern
/// final is clear; the 300 m tower stands off the south end).
/// </summary>
public sealed class StolContest
{
    public const double LineFromThresholdM = 150.0, MarkSpacingM = 3.048, MarkedLengthM = 3.048 * 60;   // marks every 10 ft for 600 ft
    public const int Rounds = 3;
    public const double HeadingRad = System.Math.PI;   // southbound
    public enum Kinds { Landing, Takeoff }
    public enum Phases { Approach, Rolling, Stopped, TakeoffRoll, Airborne, Foul, Finished }

    public readonly double LineX, StripY, StripHalfW, FarEndX, NorthThresholdX;
    public int Attempt { get; private set; }               // 0..5: L, T, L, T, L, T
    public Kinds Kind => Attempt % 2 == 0 ? Kinds.Landing : Kinds.Takeoff;
    public Phases Phase { get; private set; }
    public double TouchdownPastLineM { get; private set; }
    /// <summary>Where the judge's flagger stands for the last finished attempt (metres past the line), null before.</summary>
    public double? FlagPastLineM { get; private set; }
    public string Message { get; private set; } = "";
    public readonly System.Collections.Generic.List<double> Landings = new(), Takeoffs = new();
    private double _stillSec, _airSec, _lastContact;

    public StolContest(double northThresholdX, double stripY, double stripHalfWidth, double farEndX)
    {
        NorthThresholdX = northThresholdX; LineX = northThresholdX - LineFromThresholdM;
        StripY = stripY; StripHalfW = stripHalfWidth; FarEndX = farEndX;
        BeginAttempt();
    }

    public static StolContest ForAirport(WorldTerrain.Airport a)
    {
        WorldTerrain.Strip g = System.Array.Find(WorldTerrain.AirportStrips, s => s.Kind == "grass");
        double cx = a.X + g.Dx;
        return new StolContest(cx + g.Length / 2, a.Y + g.Dy, g.Width / 2 + 2, cx - g.Length / 2);
    }

    /// <summary>Metres past the line (southbound) of a point.</summary>
    public double PastLine(double x) => LineX - x;
    /// <summary>World x of a point <paramref name="pastLineM"/> past the line.</summary>
    public double XAt(double pastLineM) => LineX - pastLineM;

    public void BeginAttempt()
    {
        Phase = Kind == Kinds.Landing ? Phases.Approach : Phases.TakeoffRoll;
        _stillSec = 0; _airSec = 0; _lastContact = 0; TouchdownPastLineM = 0;
        Message = Kind == Kinds.Landing ? $"Landing {Attempt / 2 + 1} of {Rounds}: touch down at or past the line, stop short"
                                        : $"Takeoff {Attempt / 2 + 1} of {Rounds}: from the line — lift off as short as you can";
    }

    /// <param name="mains">the main wheels' mean position (NED)</param>
    public void Update(Vec3 mains, bool mainsOnGround, double groundSpeedMs, double dt)
    {
        if (Phase is Phases.Stopped or Phases.Airborne or Phases.Foul or Phases.Finished) return;
        double past = PastLine(mains.X);
        bool offStrip = System.Math.Abs(mains.Y - StripY) > StripHalfW;
        switch (Phase)
        {
            case Phases.Approach:
                if (!mainsOnGround) break;
                TouchdownPastLineM = past;
                if (offStrip) { Foul("touched down off the strip"); break; }
                if (past < 0) { Foul($"touched down {-past * 3.28084:F0} ft SHORT of the line"); break; }
                Phase = Phases.Rolling; Message = $"Touchdown +{past * 3.28084:F0} ft — stop!";
                break;
            case Phases.Rolling:
                if (offStrip || mains.X < FarEndX) { Foul("ran off the strip"); break; }
                _stillSec = groundSpeedMs < 0.3 && mainsOnGround ? _stillSec + dt : 0;
                if (_stillSec > 1.0) { Phase = Phases.Stopped; FlagPastLineM = past; Landings.Add(past); Message = $"Landing {Landings.Count}: {past * 3.28084:F0} ft"; }
                break;
            case Phases.TakeoffRoll:
                if (offStrip || mains.X < FarEndX) { Foul("ran off the strip"); break; }
                if (mainsOnGround) { _airSec = 0; _lastContact = past; }
                else if ((_airSec += dt) >= 1.0 && _lastContact > 0)
                {
                    Phase = Phases.Airborne; FlagPastLineM = _lastContact; Takeoffs.Add(_lastContact);
                    Message = $"Takeoff {Takeoffs.Count}: {_lastContact * 3.28084:F0} ft";
                }
                break;
        }
    }

    private void Foul(string why) { Phase = Phases.Foul; Message = $"FOUL — {why}. Fly it again."; }

    public bool AttemptDone => Phase is Phases.Stopped or Phases.Airborne or Phases.Foul;

    /// <summary>On to the next attempt (a foul repeats this one). Returns false when the contest is over.</summary>
    public bool Next()
    {
        if (Phase != Phases.Foul) Attempt++;
        if (Attempt >= 2 * Rounds) { Phase = Phases.Finished; Message = $"FINAL {TotalFt:F0} ft  (takeoff {AvgTakeoffM * 3.28084:F0} + landing {AvgLandingM * 3.28084:F0})"; return false; }
        BeginAttempt();
        return true;
    }

    public double AvgLandingM => Landings.Count > 0 ? System.Linq.Enumerable.Average(Landings) : 0;
    public double AvgTakeoffM => Takeoffs.Count > 0 ? System.Linq.Enumerable.Average(Takeoffs) : 0;
    public double TotalFt => (AvgLandingM + AvgTakeoffM) * 3.28084;
}

/// <summary>The farmer's field east of each runway (past the aerobatic box): a ploughed rectangle along the runway
/// heading with a power line crossing it 100 yards from the south end. The wires sag to 100 ft AGL at mid-span —
/// a crop duster crosses the field UNDER them. One instance per plateau (<see cref="For"/>); the Valley's is
/// <see cref="Valley"/>.</summary>
public sealed class CropField
{
    public static readonly CropField[] All = BuildAll();
    public static CropField Valley => All[0];
    public static CropField For(int plateau) => All[System.Math.Clamp(plateau, 0, All.Length - 1)];
    private static CropField[] BuildAll()
    {
        var a = new CropField[WorldTerrain.PlateauCount];
        for (int p = 0; p < a.Length; p++) a[p] = new CropField(p);
        return a;
    }

    public readonly int Plateau;
    public WorldTerrain.Airport Home => WorldTerrain.Airports[Plateau];
    private CropField(int plateau) { Plateau = plateau; }

    public double X0 => Home.X - 300;                                    // 600 m long (along x), east of the aerobatic box
    public double X1 => Home.X + 300;
    public double Y0 => Home.Y + 1250;                                   // 300 m wide (2026-10-05: in by 350 m)
    public double Y1 => Home.Y + 1550;
    public double ElevationM => Home.ElevationM;
    public const double CellM = 5.0;
    public const int CellsX = 120, CellsY = 60;                          // 600 × 300 m of 5 m cells (3 across a swath)

    public double WireX => X1 - 91.44;                                   // 100 yards in from the NORTH end (2026-10-07: the runs come in from the north — the city fills the south)
    public const double PoleOffsetM = 40.0;                              // poles stand this far outside the field edges
    public double PoleY0 => Y0 - PoleOffsetM;
    public double PoleY1 => Y1 + PoleOffsetM;
    public const double PoleHeightM = 42.0;                              // wire attachment height AGL at the poles
    public const double WireLowestAglM = 30.48;                          // 100 ft at mid-span
    public const double WireSpacingM = 2.5;                              // three conductors on the crossarm (along x)

    public bool Inside(double x, double y) => x >= X0 && x <= X1 && y >= Y0 && y <= Y1;

    /// <summary>Conductor height AGL at lateral position y (parabolic sag between the poles).</summary>
    public double WireAglAt(double y)
    {
        double mid = (PoleY0 + PoleY1) / 2, half = (PoleY1 - PoleY0) / 2;
        double u = System.Math.Clamp((y - mid) / half, -1.0, 1.0);
        return WireLowestAglM + (PoleHeightM - WireLowestAglM) * u * u;
    }
}

/// <summary>
/// Crop dusting (owner 2026-10-07): the field is already part-sprayed in a RACETRACK pattern (the ag-nav pattern that
/// trades adjacent swaths for wide 180° turns: swath 1, then 11, then 2, 12 …), so five passes are left — 18, 9, 19, 10, 20.
/// The spray comes on by itself over the field; a stretch of a swath is CREDITED (painted neon orange) only when it is
/// flown on its line (≤ 6 m off), 5–20 ft above the crop and 80–110 kt (a Pawnee works ~96 kt with a 50 ft swath, release
/// about 10 ft up — a little generous). Outside the speed / height window time is added at a rate that grows with the
/// deviation; off the line nothing is painted and the pilot comes back for it. Score = time to finish + penalties.
/// Touching the wires (100 yards in from the north end) ends the run.
/// </summary>
public sealed class CropDust
{
    public const int Swaths = 20, PreSprayed = 15;
    public const double SwathM = 15.0;                                   // 300 m / 20 ≈ 49 ft (a Pawnee's 50 ft swath)
    public const double LineTolM = 6.0, HeightMinM = 1.524, HeightMaxM = 6.096, SpeedMinMs = 80 / 1.943844, SpeedMaxMs = 110 / 1.943844;
    public const double SprayAglM = 25.0;                                // the boom opens below this over the field
    public const double WireHitHalfBandM = 1.8;
    /// <summary>The racetrack order (0-based swaths): 0, 10, 1, 11, … 9, 19.</summary>
    public static readonly int[] Order = BuildOrder();
    private static int[] BuildOrder() { var o = new int[Swaths]; for (int k = 0; k < Swaths / 2; k++) { o[2 * k] = k; o[2 * k + 1] = k + Swaths / 2; } return o; }

    private readonly bool[,] _credited = new bool[CropField.CellsX, CropField.CellsY];
    private Vec3 _prev; private bool _havePrev;
    public CropField Field { get; }
    public CropDust(CropField? field = null)
    {
        Field = field ?? CropField.Valley;
        for (int k = 0; k < PreSprayed; k++) CreditSwath(Order[k]);
    }

    public bool Spraying { get; private set; }
    public bool InWindow { get; private set; }
    public bool WireStrike { get; private set; }
    public bool Finished { get; private set; }
    public int PassesUnder { get; private set; }
    public int CrossingsOver { get; private set; }
    public double ElapsedSec { get; private set; }
    public double PenaltySec { get; private set; }
    public double ScoreSec => ElapsedSec + PenaltySec;
    /// <summary>Signed distance from the target swath's line (m, + = east of it) and the target swath (0-based, −1 done).</summary>
    public double CrossTrackM { get; private set; }
    public int TargetSwath { get; private set; } = -1;
    public string LastEvent { get; private set; } = "Fly the swath the arrow shows — 5–20 ft, 80–110 kt";
    public bool Credited(int i, int j) => _credited[i, j];
    public System.Collections.Generic.List<(int, int)> NewlyCovered { get; } = new();
    public double Coverage { get { int n = 0; foreach (bool b in _credited) if (b) n++; return (double)n / (CropField.CellsX * CropField.CellsY); } }

    public double SwathCentreY(int k) => Field.Y0 + (k + 0.5) * SwathM;
    private int CellsPerSwath => (int)System.Math.Round(SwathM / CropField.CellM);
    public double SwathDone(int k)
    {
        int j0 = k * CellsPerSwath, n = 0, of = 0;
        for (int i = 0; i < CropField.CellsX; i++) for (int j = j0; j < j0 + CellsPerSwath; j++) { of++; if (_credited[i, j]) n++; }
        return (double)n / of;
    }
    public bool SwathComplete(int k) => SwathDone(k) >= 0.97;
    private void CreditSwath(int k) { for (int i = 0; i < CropField.CellsX; i++) for (int j = k * CellsPerSwath; j < (k + 1) * CellsPerSwath; j++) _credited[i, j] = true; }

    /// <summary>The next swath to fly: the first incomplete one in racetrack order.</summary>
    public int NextSwath() { foreach (int k in Order) if (!SwathComplete(k)) return k; return -1; }
    /// <summary>Remaining passes in the planned order, from the next one.</summary>
    public int PassesLeft { get { int n = 0; foreach (int k in Order) if (!SwathComplete(k)) n++; return n; } }
    /// <summary>Southbound first (the runs come in from the north), alternating along the racetrack.</summary>
    public bool Southbound(int k) => System.Array.IndexOf(Order, k) % 2 == PreSprayed % 2;
    /// <summary>Where the next pass starts (the field edge it enters at) and its heading — the arrow.</summary>
    public (double x, double y, double headingRad) NextPassStart()
    {
        int k = NextSwath(); if (k < 0) return (Field.X1, Field.Y0, System.Math.PI);
        bool south = Southbound(k);
        return (south ? Field.X1 : Field.X0, SwathCentreY(k), south ? System.Math.PI : 0.0);
    }

    public void Update(Vec3 pos, double aglM, double groundSpeedMs, double dt)
    {
        NewlyCovered.Clear();
        if (WireStrike || Finished) { Spraying = false; return; }
        ElapsedSec += dt;

        // Power-line crossing between the previous and this position.
        if (_havePrev && (_prev.X - Field.WireX) * (pos.X - Field.WireX) < 0)
        {
            double f = (Field.WireX - _prev.X) / (pos.X - _prev.X);
            double yc = _prev.Y + (pos.Y - _prev.Y) * f, zc = _prev.Z + (pos.Z - _prev.Z) * f;
            double agl = -zc - Field.ElevationM;
            if (yc > Field.PoleY0 && yc < Field.PoleY1)
            {
                double wire = Field.WireAglAt(yc);
                if (System.Math.Abs(agl - wire) < WireHitHalfBandM) { WireStrike = true; LastEvent = "WIRE STRIKE"; _prev = pos; return; }
                if (agl < wire) PassesUnder++; else CrossingsOver++;
            }
        }
        _prev = pos; _havePrev = true;

        TargetSwath = NextSwath();
        if (TargetSwath < 0) { Finished = true; Spraying = false; LastEvent = $"FIELD DONE  {ScoreSec:F0} s"; return; }
        CrossTrackM = pos.Y - SwathCentreY(TargetSwath);

        // The boom opens by itself whenever the aircraft is low over the field.
        Spraying = Field.Inside(pos.X, pos.Y) && aglM < SprayAglM;
        if (!Spraying) { InWindow = false; return; }
        bool heightOk = aglM >= HeightMinM && aglM <= HeightMaxM, speedOk = groundSpeedMs >= SpeedMinMs && groundSpeedMs <= SpeedMaxMs;
        InWindow = heightOk && speedOk;
        // Time docked outside the window, faster the further out (linear + quadratic in the normalised deviation).
        double dh = aglM < HeightMinM ? (HeightMinM - aglM) / 1.5 : aglM > HeightMaxM ? (aglM - HeightMaxM) / 1.5 : 0;
        double dv = groundSpeedMs < SpeedMinMs ? (SpeedMinMs - groundSpeedMs) / 2.5 : groundSpeedMs > SpeedMaxMs ? (groundSpeedMs - SpeedMaxMs) / 2.5 : 0;
        double e = dh + dv;
        PenaltySec += (e + e * e) * dt;

        // Credit: on a swath's line (whichever is nearest), in the window — the whole swath width at this point.
        int k = (int)System.Math.Floor((pos.Y - Field.Y0) / SwathM);
        if (k < 0 || k >= Swaths) return;
        double off = pos.Y - SwathCentreY(k);
        if (!InWindow) { LastEvent = !heightOk ? (aglM > HeightMaxM ? "too HIGH — no credit" : "too LOW") : (groundSpeedMs > SpeedMaxMs ? "too FAST — no credit" : "too SLOW — no credit"); return; }
        if (System.Math.Abs(off) > LineTolM) { LastEvent = $"{System.Math.Abs(off):F0} m off the line — no credit"; return; }
        LastEvent = "spraying — on the line";
        int i0 = (int)System.Math.Floor((pos.X - CropField.CellM - Field.X0) / CropField.CellM), i1 = (int)System.Math.Floor((pos.X + CropField.CellM - Field.X0) / CropField.CellM);
        for (int i = System.Math.Max(0, i0); i <= System.Math.Min(CropField.CellsX - 1, i1); i++)
            for (int j = k * CellsPerSwath; j < (k + 1) * CellsPerSwath; j++)
                if (!_credited[i, j]) { _credited[i, j] = true; NewlyCovered.Add((i, j)); }
    }
}

using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>Marked IAC aerobatic box beside the Valley runway (its west edge 500 m east of the centreline):
/// 1,000 m square, floor 328 ft, ceiling 3,500 ft — drawn as a glass box in the sky.</summary>
public static class AeroBox
{
    public const double CenterX = 400, CenterY = 1000, SizeM = 1000.0;
    public const double FloorAglM = 100.0, CeilingAglM = 1067.0;
    public static bool Inside(Vec3 pos)
    {
        double agl = -pos.Z - WorldTerrain.GroundHeightAt(pos.X, pos.Y);
        return System.Math.Abs(pos.X - CenterX) <= SizeM / 2 && System.Math.Abs(pos.Y - CenterY) <= SizeM / 2
               && agl >= FloorAglM && agl <= CeilingAglM;
    }
}

/// <summary>One element of the air-racing course: an air gate (two pylons, fly between, level, below the
/// top) or a single turning pylon that must be kept on the stated side.</summary>
public sealed class RaceElement
{
    public enum Kinds { Gate, PylonOnLeft, PylonOnRight }
    public Kinds Kind;
    public double X, Y, HeadingDeg;      // position; heading = required direction of travel through it
    public const double GateHalfWidthM = 15.24, GateHeightM = 75.0, PylonRadiusM = 1.5;   // pylons 100 ft apart, 75 m tall
    public const double NumberAglM = 91.44;                                                // the rotating numbers, 300 ft up

    public Vec3 Forward => new(System.Math.Cos(HeadingDeg * System.Math.PI / 180), System.Math.Sin(HeadingDeg * System.Math.PI / 180), 0);
    public Vec3 Right => new(-System.Math.Sin(HeadingDeg * System.Math.PI / 180), System.Math.Cos(HeadingDeg * System.Math.PI / 180), 0);
    public Vec3 Centre => new(X, Y, 0);
}

/// <summary>"Air Racing" (owner request; no brand names): a COMPACT track east of the Valley runway, laid out the
/// way the pylon-racing world championship tracks are — start gate, a gate, a three-pylon chicane, a gate, a
/// vertical turning pylon at the far end, then back through two gates and a chicane to a separate finish gate.
/// About 750 × 350 m. Elements are taken IN ORDER; the rotating numbers 300 ft above each element show the way.</summary>
public static class RaceCourse
{
    public static readonly RaceElement[] Elements =
    {
        new() { Kind = RaceElement.Kinds.Gate, X = 850, Y = 1800, HeadingDeg = 0 },          // 1 START, northbound
        new() { Kind = RaceElement.Kinds.Gate, X = 1050, Y = 1800, HeadingDeg = 0 },         // 2
        new() { Kind = RaceElement.Kinds.PylonOnRight, X = 1180, Y = 1770, HeadingDeg = 0 }, // 3 chicane
        new() { Kind = RaceElement.Kinds.PylonOnLeft, X = 1300, Y = 1830, HeadingDeg = 0 },  // 4
        new() { Kind = RaceElement.Kinds.PylonOnRight, X = 1420, Y = 1770, HeadingDeg = 0 }, // 5
        new() { Kind = RaceElement.Kinds.Gate, X = 1520, Y = 1800, HeadingDeg = 0 },         // 6
        new() { Kind = RaceElement.Kinds.PylonOnRight, X = 1560, Y = 1920, HeadingDeg = 90 },// 7 vertical turning pylon: around it and back south
        new() { Kind = RaceElement.Kinds.Gate, X = 1500, Y = 2050, HeadingDeg = 180 },       // 8 southbound
        new() { Kind = RaceElement.Kinds.Gate, X = 1300, Y = 2050, HeadingDeg = 180 },       // 9
        new() { Kind = RaceElement.Kinds.PylonOnLeft, X = 1180, Y = 2080, HeadingDeg = 180 },// 10 chicane
        new() { Kind = RaceElement.Kinds.PylonOnRight, X = 1060, Y = 2020, HeadingDeg = 180 },// 11
        new() { Kind = RaceElement.Kinds.Gate, X = 900, Y = 2050, HeadingDeg = 180 },        // 12 FINISH
    };

    /// <summary>Every pylon on the course: element index, side (−1 left / +1 right of a gate, 0 for a single pylon)
    /// and its base position (x, y).</summary>
    public static List<(int element, int side, double x, double y)> Pylons()
    {
        var list = new List<(int, int, double, double)>();
        for (int i = 0; i < Elements.Length; i++)
        {
            RaceElement e = Elements[i];
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
    private readonly Dictionary<(int, int), double> _strikeCooldown = new();
    public const double PylonStrikeCooldownSec = 4.0;

    /// <summary>Wing-tip-to-wing-tip line against every pylon (2-D, below the pylon top): the first pylon it cuts
    /// through is struck. Returns (element, side) once per pylon per few seconds; +3 s while racing.</summary>
    public (int element, int side)? CheckPylonStrike(Vec3 leftTip, Vec3 rightTip, double dt)
    {
        foreach (var k in new List<(int, int)>(_strikeCooldown.Keys)) { _strikeCooldown[k] -= dt; if (_strikeCooldown[k] <= 0) _strikeCooldown.Remove(k); }
        foreach ((int el, int side, double px, double py) in RaceCourse.Pylons())
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

    public AirRace(RaceElement[]? elements = null) { _els = elements ?? RaceCourse.Elements; }

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


/// <summary>The farmer's field east of the Valley runway (past the aerobatic box): a ploughed rectangle along the runway heading with a
/// power line crossing it 100 yards from the south end. The wires sag to 100 ft AGL at mid-span — a crop
/// duster crosses the field UNDER them.</summary>
public static class CropField
{
    public static WorldTerrain.Airport Home => WorldTerrain.Airports[0];
    public static double X0 => Home.X - 300;                             // 600 m long (along x), east of the aerobatic box
    public static double X1 => Home.X + 300;
    public static double Y0 => Home.Y + 1600;                            // 300 m wide
    public static double Y1 => Home.Y + 1900;
    public static double ElevationM => Home.ElevationM;
    public const double CellM = 10.0;
    public static int CellsX => (int)System.Math.Round((X1 - X0) / CellM);
    public static int CellsY => (int)System.Math.Round((Y1 - Y0) / CellM);

    public static double WireX => X0 + 91.44;                            // 100 yards from the south end
    public const double PoleOffsetM = 40.0;                              // poles stand this far outside the field edges
    public static double PoleY0 => Y0 - PoleOffsetM;
    public static double PoleY1 => Y1 + PoleOffsetM;
    public const double PoleHeightM = 42.0;                              // wire attachment height AGL at the poles
    public const double WireLowestAglM = 30.48;                          // 100 ft at mid-span
    public const double WireSpacingM = 2.5;                              // three conductors on the crossarm (along x)

    public static bool Inside(double x, double y) => x >= X0 && x <= X1 && y >= Y0 && y <= Y1;

    /// <summary>Conductor height AGL at lateral position y (parabolic sag between the poles).</summary>
    public static double WireAglAt(double y)
    {
        double mid = (PoleY0 + PoleY1) / 2, half = (PoleY1 - PoleY0) / 2;
        double u = System.Math.Clamp((y - mid) / half, -1.0, 1.0);
        return WireLowestAglM + (PoleHeightM - WireLowestAglM) * u * u;
    }
}

/// <summary>Crop-dusting run: spray covers the field cells under the aircraft while it is low over the field;
/// every crossing of the power line UNDER the wires is a pass; touching a wire ends the run.</summary>
public sealed class CropDust
{
    public const double SprayMaxAglM = 6.0, SwathHalfWidthM = 8.0, MinSpraySpeedMs = 12.0;
    public const double WireHitHalfBandM = 1.8;   // vertical tolerance for a strike (airframe height)

    private readonly bool[,] _covered = new bool[CropField.CellsX, CropField.CellsY];
    private int _coveredCount;
    private Vec3 _prev; private bool _havePrev;

    public bool Spraying { get; private set; }
    public int PassesUnder { get; private set; }
    public int CrossingsOver { get; private set; }
    public bool WireStrike { get; private set; }
    public double Coverage => (double)_coveredCount / (CropField.CellsX * CropField.CellsY);
    public string LastEvent { get; private set; } = "Spray the field — fly UNDER the wires";
    public bool Covered(int i, int j) => _covered[i, j];
    /// <summary>Cells newly covered since the last call (for the visual), as (i, j) pairs.</summary>
    public System.Collections.Generic.List<(int, int)> NewlyCovered { get; } = new();

    public void Update(Vec3 pos, double aglM, double groundSpeedMs)
    {
        NewlyCovered.Clear();
        if (WireStrike) { Spraying = false; return; }

        // Power-line crossing between the previous and this position.
        if (_havePrev && (_prev.X - CropField.WireX) * (pos.X - CropField.WireX) < 0)
        {
            double f = (CropField.WireX - _prev.X) / (pos.X - _prev.X);
            double yc = _prev.Y + (pos.Y - _prev.Y) * f;
            double zc = _prev.Z + (pos.Z - _prev.Z) * f;
            double agl = -zc - CropField.ElevationM;
            if (yc > CropField.PoleY0 && yc < CropField.PoleY1)
            {
                double wire = CropField.WireAglAt(yc);
                if (System.Math.Abs(agl - wire) < WireHitHalfBandM)
                {
                    WireStrike = true; Spraying = false; LastEvent = "HIT THE WIRES";
                    _prev = pos; return;
                }
                if (agl < wire) { PassesUnder++; LastEvent = $"under the wires — pass {PassesUnder}"; }
                else { CrossingsOver++; LastEvent = "over the wires — no credit, get UNDER them"; }
            }
        }
        _prev = pos; _havePrev = true;

        Spraying = CropField.Inside(pos.X, pos.Y) && aglM > 0.2 && aglM < SprayMaxAglM && groundSpeedMs > MinSpraySpeedMs;
        if (!Spraying) return;
        int i0 = (int)System.Math.Floor((pos.X - SwathHalfWidthM - CropField.X0) / CropField.CellM), i1 = (int)System.Math.Floor((pos.X + SwathHalfWidthM - CropField.X0) / CropField.CellM);
        int j0 = (int)System.Math.Floor((pos.Y - SwathHalfWidthM - CropField.Y0) / CropField.CellM), j1 = (int)System.Math.Floor((pos.Y + SwathHalfWidthM - CropField.Y0) / CropField.CellM);
        for (int i = System.Math.Max(0, i0); i <= System.Math.Min(CropField.CellsX - 1, i1); i++)
        for (int j = System.Math.Max(0, j0); j <= System.Math.Min(CropField.CellsY - 1, j1); j++)
        {
            double cx = CropField.X0 + (i + 0.5) * CropField.CellM, cy = CropField.Y0 + (j + 0.5) * CropField.CellM;
            if ((cx - pos.X) * (cx - pos.X) + (cy - pos.Y) * (cy - pos.Y) > SwathHalfWidthM * SwathHalfWidthM) continue;
            if (_covered[i, j]) continue;
            _covered[i, j] = true; _coveredCount++; NewlyCovered.Add((i, j));
        }
    }
}

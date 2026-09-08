using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>Marked IAC aerobatic box on the Valley plain: 1,000 m square, floor 328 ft, ceiling 3,500 ft.</summary>
public static class AeroBox
{
    public const double CenterX = -2200, CenterY = 1200, SizeM = 1000.0;
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
    public const double GateHalfWidthM = 7.0, GateHeightM = 25.0, PylonRadiusM = 1.5;

    public Vec3 Forward => new(System.Math.Cos(HeadingDeg * System.Math.PI / 180), System.Math.Sin(HeadingDeg * System.Math.PI / 180), 0);
    public Vec3 Right => new(-System.Math.Sin(HeadingDeg * System.Math.PI / 180), System.Math.Cos(HeadingDeg * System.Math.PI / 180), 0);
    public Vec3 Centre => new(X, Y, 0);
}

/// <summary>"Air Racing" (owner request; no brand names): a low-level loop on the Valley plain that crosses
/// the gorge twice. Elements must be taken IN ORDER; numbered cloud hoops between them are optional guides.</summary>
public static class RaceCourse
{
    public static readonly RaceElement[] Elements =
    {
        new() { Kind = RaceElement.Kinds.Gate, X = 0, Y = 2000, HeadingDeg = 0 },            // 1 start/finish, northbound
        new() { Kind = RaceElement.Kinds.Gate, X = 1300, Y = 2000, HeadingDeg = 0 },         // 2
        new() { Kind = RaceElement.Kinds.PylonOnRight, X = 2900, Y = 2150, HeadingDeg = 45 }, // 3 turn east around it
        new() { Kind = RaceElement.Kinds.Gate, X = 3000, Y = 2700, HeadingDeg = 90 },        // 4 eastbound
        new() { Kind = RaceElement.Kinds.Gate, X = 3000, Y = 3300, HeadingDeg = 90 },        // 5
        new() { Kind = RaceElement.Kinds.PylonOnRight, X = 3100, Y = 3750, HeadingDeg = 135 },// 6 turn south
        new() { Kind = RaceElement.Kinds.Gate, X = 2400, Y = 3650, HeadingDeg = 180 },       // 7 southbound
        new() { Kind = RaceElement.Kinds.PylonOnLeft, X = 1750, Y = 3580, HeadingDeg = 180 }, // 8 chicane
        new() { Kind = RaceElement.Kinds.PylonOnRight, X = 1450, Y = 3720, HeadingDeg = 180 },// 9 chicane
        new() { Kind = RaceElement.Kinds.Gate, X = 800, Y = 3650, HeadingDeg = 180 },        // 10
        new() { Kind = RaceElement.Kinds.PylonOnRight, X = -350, Y = 3550, HeadingDeg = 225 },// 11 turn west
        new() { Kind = RaceElement.Kinds.Gate, X = -450, Y = 3000, HeadingDeg = 270 },       // 12 westbound
        new() { Kind = RaceElement.Kinds.Gate, X = -450, Y = 2450, HeadingDeg = 270 },       // 13 → back to 1
    };

    public const double HoopAglM = 45.0, HoopRadiusM = 30.0;

    /// <summary>Guide hoops: midpoint of each leg (element k → k+1, wrapping to the start), numbered 1..N.</summary>
    public static (Vec3 pos, double headingDeg, int number)[] Hoops()
    {
        int n = Elements.Length;
        var hoops = new (Vec3, double, int)[n];
        for (int i = 0; i < n; i++)
        {
            RaceElement a = Elements[i], b = Elements[(i + 1) % n];
            double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
            double hdg = System.Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / System.Math.PI;
            double ground = WorldTerrain.GroundHeightAt(mx, my);
            hoops[i] = (new Vec3(mx, my, -(ground + HoopAglM)), hdg, i + 1);
        }
        return hoops;
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
                if (Next == 0)
                {
                    if (!Running) { Running = true; ElapsedSec = 0; PenaltySec = 0; LastEvent = "GO"; }
                    else { Finished = true; Running = false; LastEvent = $"FINISH {TotalSec:F1} s"; }
                }
                if (!Finished) Next = (Next + 1) % _els.Length;
            }
        }
        _prev = pos;
    }
}

/// <summary>STOL contest on a dirt strip: land AT or AFTER the line, stop short. Score = stop distance past the
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
        WorldTerrain.Strip dirt = System.Array.Find(WorldTerrain.AirportStrips, s => s.Kind == "dirt");
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
